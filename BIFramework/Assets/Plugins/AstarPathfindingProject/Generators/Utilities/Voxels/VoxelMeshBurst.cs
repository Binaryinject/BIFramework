using UnityEngine;
using Unity.Collections;
using Pathfinding.Voxels;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

namespace Pathfinding.Voxels.Burst {
	using Pathfinding.Jobs;

	/// <summary>VoxelMesh used for recast graphs.</summary>
	public struct VoxelMesh : IArenaDisposable {
		/// <summary>Vertices of the mesh</summary>
		public NativeList<Int3> verts;

		/// <summary>
		/// Triangles of the mesh.
		/// Each element points to a vertex in the <see cref="verts"/> array
		/// </summary>
		public NativeList<int> tris;

		/// <summary>Area index for each triangle</summary>
		public NativeList<int> areas;

		void IArenaDisposable.DisposeWith (DisposeArena arena) {
			arena.Add(verts);
			arena.Add(tris);
			arena.Add(areas);
		}
	}

	/// <summary>Builds a polygon mesh from a contour set.</summary>
	[BurstCompile]
	public struct JobBuildMesh : IJob {
		public NativeList<int> contourVertices;
		/// <summary>contour set to build a mesh from.</summary>
		public NativeList<VoxelContour> contours;
		/// <summary>Results will be written to this mesh.</summary>
		public VoxelMesh mesh;
		public CompactVoxelField field;

		/// <summary>
		/// Returns T iff (v_i, v_j) is a proper internal
		/// diagonal of P.
		/// </summary>
		static bool Diagonal (int i, int j, int n, NativeArray<int> verts, NativeArray<int> indices) {
			return InCone(i, j, n, verts, indices) && Diagonalie(i, j, n, verts, indices);
		}

		static bool InCone (int i, int j, int n, NativeArray<int> verts, NativeArray<int> indices) {
			int pi = (indices[i] & 0x0fffffff) * 4;
			int pj = (indices[j] & 0x0fffffff) * 4;
			int pi1 = (indices[Next(i, n)] & 0x0fffffff) * 4;
			int pin1 = (indices[Prev(i, n)] & 0x0fffffff) * 4;

			// If P[i] is a convex vertex [ i+1 left or on (i-1,i) ].
			if (LeftOn(pin1, pi, pi1, verts))
				return Left(pi, pj, pin1, verts) && Left(pj, pi, pi1, verts);
			// Assume (i-1,i,i+1) not collinear.
			// else P[i] is reflex.
			return !(LeftOn(pi, pj, pi1, verts) && LeftOn(pj, pi, pin1, verts));
		}

		/// <summary>
		/// Returns true iff c is strictly to the left of the directed
		/// line through a to b.
		/// </summary>
		static bool Left (int a, int b, int c, NativeArray<int> verts) {
			return Area2(a, b, c, verts) < 0;
		}

		static bool LeftOn (int a, int b, int c, NativeArray<int> verts) {
			return Area2(a, b, c, verts) <= 0;
		}

		static bool Collinear (int a, int b, int c, NativeArray<int> verts) {
			return Area2(a, b, c, verts) == 0;
		}

		public static int Area2 (int a, int b, int c, NativeArray<int> verts) {
			return (verts[b] - verts[a]) * (verts[c+2] - verts[a+2]) - (verts[c+0] - verts[a+0]) * (verts[b+2] - verts[a+2]);
		}

		/// <summary>
		/// Returns T iff (v_i, v_j) is a proper internal *or* external
		/// diagonal of P, *ignoring edges incident to v_i and v_j*.
		/// </summary>
		static bool Diagonalie (int i, int j, int n, NativeArray<int> verts, NativeArray<int> indices) {
			int d0 = (indices[i] & 0x0fffffff) * 4;
			int d1 = (indices[j] & 0x0fffffff) * 4;

			/*int a = (i+1) % indices.Length;
			 * if (a == j) a = (i-1 + indices.Length) % indices.Length;
			 * int a_v = (indices[a] & 0x0fffffff) * 4;
			 *
			 * if (a != j && Collinear (d0,a_v,d1,verts)) {
			 *  return false;
			 * }*/

			// For each edge (k,k+1) of P
			for (int k = 0; k < n; k++) {
				int k1 = Next(k, n);
				// Skip edges incident to i or j
				if (!((k == i) || (k1 == i) || (k == j) || (k1 == j))) {
					int p0 = (indices[k] & 0x0fffffff) * 4;
					int p1 = (indices[k1] & 0x0fffffff) * 4;

					if (Vequal(d0, p0, verts) || Vequal(d1, p0, verts) || Vequal(d0, p1, verts) || Vequal(d1, p1, verts))
						continue;

					if (Intersect(d0, d1, p0, p1, verts))
						return false;
				}
			}


			return true;
		}

		//	Exclusive or: true iff exactly one argument is true.
		//	The arguments are negated to ensure that they are 0/1
		//	values.  Then the bitwise Xor operator may apply.
		//	(This idea is due to Michael Baldwin.)
		static bool Xorb (bool x, bool y) {
			return !x ^ !y;
		}

		//	Returns true iff ab properly intersects cd: they share
		//	a point interior to both segments.  The properness of the
		//	intersection is ensured by using strict leftness.
		static bool IntersectProp (int a, int b, int c, int d, NativeArray<int> verts) {
			// Eliminate improper cases.
			if (Collinear(a, b, c, verts) || Collinear(a, b, d, verts) ||
				Collinear(c, d, a, verts) || Collinear(c, d, b, verts))
				return false;

			return Xorb(Left(a, b, c, verts), Left(a, b, d, verts)) && Xorb(Left(c, d, a, verts), Left(c, d, b, verts));
		}

		// Returns T iff (a,b,c) are collinear and point c lies
		// on the closed segement ab.
		static bool Between (int a, int b, int c, NativeArray<int> verts) {
			if (!Collinear(a, b, c, verts))
				return false;
			// If ab not vertical, check betweenness on x; else on y.
			if (verts[a+0] != verts[b+0])
				return ((verts[a+0] <= verts[c+0]) && (verts[c+0] <= verts[b+0])) || ((verts[a+0] >= verts[c+0]) && (verts[c+0] >= verts[b+0]));
			else
				return ((verts[a+2] <= verts[c+2]) && (verts[c+2] <= verts[b+2])) || ((verts[a+2] >= verts[c+2]) && (verts[c+2] >= verts[b+2]));
		}

		// Returns true iff segments ab and cd intersect, properly or improperly.
		static bool Intersect (int a, int b, int c, int d, NativeArray<int> verts) {
			if (IntersectProp(a, b, c, d, verts))
				return true;
			else if (Between(a, b, c, verts) || Between(a, b, d, verts) ||
					 Between(c, d, a, verts) || Between(c, d, b, verts))
				return true;
			else
				return false;
		}

		static bool Vequal (int a, int b, NativeArray<int> verts) {
			return verts[a+0] == verts[b+0] && verts[a+2] == verts[b+2];
		}

		/// <summary>(i-1+n) % n assuming 0 <= i < n</summary>
		static int Prev (int i, int n) { return i-1 >= 0 ? i-1 : n-1; }
		/// <summary>(i+1) % n assuming 0 <= i < n</summary>
		static int Next (int i, int n) { return i+1 < n ? i+1 : 0; }

		public void Execute () {
			// Maximum allowed vertices per polygon. Currently locked to 3.
			var nvp = 3;

			int maxVertices = 0;
			int maxTris = 0;
			int maxVertsPerCont = 0;

			for (int i = 0; i < contours.Length; i++) {
				// Skip null contours.
				if (contours[i].nverts < 3) continue;

				maxVertices += contours[i].nverts;
				maxTris += contours[i].nverts - 2;
				maxVertsPerCont = System.Math.Max(maxVertsPerCont, contours[i].nverts);
			}

			mesh.verts.ResizeUninitialized(maxVertices);
			mesh.tris.ResizeUninitialized(maxTris*nvp);
			mesh.areas.ResizeUninitialized(maxTris);
			var verts = mesh.verts;
			var polys = mesh.tris;
			var areas = mesh.areas;

			var indices = new NativeArray<int>(maxVertsPerCont, Allocator.Temp);
			var tris = new NativeArray<int>(maxVertsPerCont*3, Allocator.Temp);

			int vertexIndex = 0;
			int polyIndex = 0;
			int areaIndex = 0;

			for (int i = 0; i < contours.Length; i++) {
				VoxelContour cont = contours[i];

				// Skip degenerate contours
				if (cont.nverts < 3) {
					continue;
				}

				for (int j = 0; j < cont.nverts; j++) {
					indices[j] = j;
					// Convert the z coordinate from the form z*voxelArea.width which is used in other places for performance
					contourVertices[cont.vertexStartIndex + j*4+2] /= field.width;
				}

				// Triangulate the contour
				int ntris = Triangulate(cont.nverts, contourVertices.AsArray().GetSubArray(cont.vertexStartIndex, cont.nverts*4), ref indices, ref tris);

				// Assign the correct vertex indices
				int startIndex = vertexIndex;
				for (int j = 0; j < ntris*3; polyIndex++, j++) {
					//@Error sometimes
					polys[polyIndex] = tris[j]+startIndex;
				}

				// Mark all triangles generated by this contour
				// as having the area cont.area
				for (int j = 0; j < ntris; areaIndex++, j++) {
					areas[areaIndex] = cont.area;
				}

				// Copy the vertex positions
				for (int j = 0; j < cont.nverts; vertexIndex++, j++) {
					verts[vertexIndex] = new Int3(
						contourVertices[cont.vertexStartIndex + j*4],
						contourVertices[cont.vertexStartIndex + j*4+1],
						contourVertices[cont.vertexStartIndex + j*4+2]
						);
				}
			}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			if (vertexIndex != mesh.verts.Length) throw new System.Exception("Ended up at an unexpected vertex index");
			if (areaIndex > mesh.areas.Length) throw new System.Exception("Ended up at an unexpected area index");
			if (polyIndex > mesh.tris.Length) throw new System.Exception("Ended up at an unexpected poly index");
#endif

			// polyIndex might in rare cases not be equal to mesh.tris.Length.
			// This can happen if degenerate triangles were generated.
			// So we make sure the list is truncated to the right size here.
			mesh.tris.ResizeUninitialized(polyIndex);
			// Same thing for area index
			mesh.areas.ResizeUninitialized(areaIndex);
		}

		int Triangulate (int n, NativeArray<int> verts, ref NativeArray<int> indices, ref NativeArray<int> tris) {
			int ntris = 0;

			var dst = tris;

			int dstIndex = 0;

			// Debug code
			//int on = n;

			// The last bit of the index is used to indicate if the vertex can be removed.
			const int CanBeRemovedBit = 0x40000000;
			// Used to get only the index value, without any flag bits.
			const int IndexMask = 0x0fffffff;

			for (int i = 0; i < n; i++) {
				int i1 = Next(i, n);
				int i2 = Next(i1, n);
				if (Diagonal(i, i2, n, verts, indices)) {
					indices[i1] |= CanBeRemovedBit;
				}
			}

			while (n > 3) {
				int minLen = -1;
				int mini = -1;

				for (int q = 0; q < n; q++) {
					int q1 = Next(q, n);
					if ((indices[q1] & CanBeRemovedBit) != 0) {
						int p0 = (indices[q] & IndexMask) * 4;
						int p2 = (indices[Next(q1, n)] & IndexMask) * 4;

						int dx = verts[p2+0] - verts[p0+0];
						int dz = verts[p2+2] - verts[p0+2];


						//Squared distance
						int len = dx*dx + dz*dz;

						if (minLen < 0 || len < minLen) {
							minLen = len;
							mini = q;
						}
					}
				}

				if (mini == -1) {
					Debug.LogWarning("Degenerate triangles might have been generated.\n" +
						"Usually this is not a problem, but if you have a static level, try to modify the graph settings slightly to avoid this edge case.");

					// Can't run the debug stuff because we are likely running from a separate thread
					//for (int j=0;j<on;j++) {
					//	DrawLine (Prev(j,on),j,indices,verts,Color.red);
					//}

					// Should not happen.
					/*			printf("mini == -1 ntris=%d n=%d\n", ntris, n);
					 *          for (int i = 0; i < n; i++)
					 *          {
					 *              printf("%d ", indices[i] & IndexMask);
					 *          }
					 *          printf("\n");*/
					//yield break;
					return -ntris;
				}

				int i = mini;
				int i1 = Next(i, n);
				int i2 = Next(i1, n);


				dst[dstIndex] = indices[i] & IndexMask;
				dstIndex++;
				dst[dstIndex] = indices[i1] & IndexMask;
				dstIndex++;
				dst[dstIndex] = indices[i2] & IndexMask;
				dstIndex++;
				ntris++;

				// Removes P[i1] by copying P[i+1]...P[n-1] left one index.
				n--;
				for (int k = i1; k < n; k++) {
					indices[k] = indices[k+1];
				}

				if (i1 >= n) i1 = 0;
				i = Prev(i1, n);
				// Update diagonal flags.
				if (Diagonal(Prev(i, n), i1, n, verts, indices)) {
					indices[i] |= CanBeRemovedBit;
				} else {
					indices[i] &= IndexMask;
				}
				if (Diagonal(i, Next(i1, n), n, verts, indices)) {
					indices[i1] |= CanBeRemovedBit;
				} else {
					indices[i1] &= IndexMask;
				}
			}

			dst[dstIndex] = indices[0] & IndexMask;
			dstIndex++;
			dst[dstIndex] = indices[1] & IndexMask;
			dstIndex++;
			dst[dstIndex] = indices[2] & IndexMask;
			dstIndex++;
			ntris++;

			return ntris;
		}
	}
}
