using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Profiling;

namespace Pathfinding.Voxels.Burst {
	/// <summary>VoxelContourSet used for recast graphs.</summary>
	public class VoxelContourSet {
	}

	/// <summary>VoxelContour used for recast graphs.</summary>
	public struct VoxelContour {
		public int nverts;
		public int vertexStartIndex;         // Vertex coordinates, each vertex contains 4 components.
		// public int[] rverts;        // Raw vertex coordinates, each vertex contains 4 components.

		public int reg;             // Region ID of the contour.
		public int area;            // Area ID of the contour.
	}

	[BurstCompile(CompileSynchronously = true)]
	public struct JobBuildContours : IJob {
		public CompactVoxelField field;
		public float maxError;
		public float maxEdgeLength;
		public int buildFlags;
		public float cellSize;
		public NativeList<VoxelContour> outputContours;
		public NativeList<int> outputVerts;

		public void Execute () {
			outputContours.Clear();
			outputVerts.Clear();

			int w = field.width;
			int d = field.depth;

			int wd = w*d;

			//cset.bounds = field.bounds;


			const ushort BorderReg = VoxelUtilityBurst.BorderReg;

			//cset.nconts = 0;

			//NOTE: This array may contain any data, but since we explicitly set all data in it before we use it, it's OK.
			var flags = new NativeArray<ushort>(field.spans.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			// Mark boundaries. (@?)
			for (int z = 0; z < wd; z += field.width) {
				for (int x = 0; x < field.width; x++) {
					CompactVoxelCell c = field.cells[x+z];

					for (int i = (int)c.index, ci = (int)(c.index+c.count); i < ci; i++) {
						ushort res = 0;
						CompactVoxelSpan s = field.spans[i];

						if (s.reg == 0 || (s.reg & BorderReg) == BorderReg) {
							flags[i] = 0;
							continue;
						}

						for (int dir = 0; dir < 4; dir++) {
							int r = 0;

							if (s.GetConnection(dir) != CompactVoxelField.NotConnected) {
								int ni = (int)field.cells[field.GetNeighbourIndex(x+z, dir)].index + s.GetConnection(dir);
								r = field.spans[ni].reg;
							}

							//@TODO - Why isn't this inside the previous IF
							if (r == s.reg) {
								res |= (ushort)(1 << dir);
							}
						}

						//Inverse, mark non connected edges.
						flags[i] = (ushort)(res ^ 0xf);
					}
				}
			}


			NativeList<int> verts = new NativeList<int>(256, Allocator.Temp);
			NativeList<int> simplified = new NativeList<int>(64, Allocator.Temp);

			for (int z = 0; z < wd; z += field.width) {
				for (int x = 0; x < field.width; x++) {
					CompactVoxelCell c = field.cells[x+z];

					for (int i = (int)c.index, ci = (int)(c.index+c.count); i < ci; i++) {
						//CompactVoxelSpan s = field.spans[i];

						if (flags[i] == 0 || flags[i] == 0xf) {
							flags[i] = 0;
							continue;
						}

						int reg = field.spans[i].reg;

						if (reg == 0 || (reg & BorderReg) == BorderReg) {
							continue;
						}

						int area = field.areaTypes[i];

						verts.Clear();
						simplified.Clear();

						WalkContour(x, z, i, flags, verts);

						SimplifyContour(verts, simplified, maxError, buildFlags);
						RemoveDegenerateSegments(simplified);

						VoxelContour contour = new VoxelContour();
						contour.vertexStartIndex = outputVerts.Length;
						outputVerts.AddRange(simplified);
// #if ASTAR_RECAST_INCLUDE_RAW_VERTEX_CONTOUR
// 						//Not used at the moment, just debug stuff
// 						contour.rverts = ClaimIntArr(verts.Length);
// 						for (int j = 0; j < verts.Length; j++) contour.rverts[j] = verts[j];
// #endif
						contour.nverts = simplified.Length/4;
						contour.reg = reg;
						contour.area = area;

						outputContours.Add(contour);

						// #if ASTARDEBUG
						// for (int q = 0, j = (simplified.Length/4)-1; q < (simplified.Length/4); j = q, q++) {
						// 	int i4 = q*4;
						// 	int j4 = j*4;

						// 	Vector3 p1 = Vector3.Scale(
						// 		new Vector3(
						// 			simplified[i4+0],
						// 			simplified[i4+1],
						// 			(simplified[i4+2]/(float)field.width)
						// 			),
						// 		cellScale)
						// 				 +voxelOffset;

						// 	Vector3 p2 = Vector3.Scale(
						// 		new Vector3(
						// 			simplified[j4+0],
						// 			simplified[j4+1],
						// 			(simplified[j4+2]/(float)field.width)
						// 			)
						// 		, cellScale)
						// 				 +voxelOffset;


						// 	if (CalcAreaOfPolygon2D(contour.verts, contour.nverts) > 0) {
						// 		Debug.DrawLine(p1, p2, AstarMath.IntToColor(reg, 0.5F));
						// 	} else {
						// 		Debug.DrawLine(p1, p2, Color.red);
						// 	}
						// }
						// #endif
					}
				}
			}

			verts.Dispose();
			simplified.Dispose();



			// Check and merge droppings.
			// Sometimes the previous algorithms can fail and create several outputContours
			// per area. This pass will try to merge the holes into the main region.
			for (int i = 0; i < outputContours.Length; i++) {
				VoxelContour cont = outputContours[i];
				// Check if the contour is would backwards.
				if (CalcAreaOfPolygon2D(outputVerts, cont.vertexStartIndex, cont.nverts) < 0) {
					// Find another contour which has the same region ID.
					int mergeIdx = -1;
					for (int j = 0; j < outputContours.Length; j++) {
						if (i == j) continue;
						if (outputContours[j].nverts > 0 && outputContours[j].reg == cont.reg) {
							// Make sure the polygon is correctly oriented.
							if (CalcAreaOfPolygon2D(outputVerts, outputContours[j].vertexStartIndex, outputContours[j].nverts) > 0) {
								mergeIdx = j;
								break;
							}
						}
					}
					if (mergeIdx == -1) {
						// Debug.LogError("rcBuildContours: Could not find merge target for bad contour "+i+".");
					} else {
						// Debugging
						// Debug.LogWarning ("Fixing contour");

						VoxelContour mcont = outputContours[mergeIdx];
						// Merge by closest points.
						int ia = 0, ib = 0;
						GetClosestIndices(outputVerts, mcont.vertexStartIndex, mcont.nverts, cont.vertexStartIndex, cont.nverts, ref ia, ref ib);

						if (ia == -1 || ib == -1) {
							// Debug.LogWarning("rcBuildContours: Failed to find merge points for "+i+" and "+mergeIdx+".");
							continue;
						}


						if (!MergeContours(outputVerts, ref mcont, ref cont, ia, ib)) {
							//Debug.LogWarning("rcBuildContours: Failed to merge contours "+i+" and "+mergeIdx+".");
							continue;
						}

						outputContours[mergeIdx] = mcont;
						outputContours[i] = cont;
					}
				}
			}
		}

		void GetClosestIndices (NativeArray<int> verts, int vertexStartIndexA, int nvertsa,
								int vertexStartIndexB, int nvertsb,
								ref int ia, ref int ib) {
			int closestDist = 0xfffffff;

			ia = -1;
			ib = -1;
			for (int i = 0; i < nvertsa; i++) {
				//in is a keyword in C#, so I can't use that as a variable name
				int in2 = (i+1) % nvertsa;
				int ip = (i+nvertsa-1) % nvertsa;
				int va = vertexStartIndexA + i*4;
				int van = vertexStartIndexA + in2*4;
				int vap = vertexStartIndexA + ip*4;

				for (int j = 0; j < nvertsb; ++j) {
					int vb = vertexStartIndexB + j*4;
					// vb must be "infront" of va.
					if (Ileft(verts, vap, va, vb) && Ileft(verts, va, van, vb)) {
						int dx = verts[vb+0] - verts[va+0];
						int dz = (verts[vb+2]/field.width) - (verts[va+2]/field.width);
						int d = dx*dx + dz*dz;
						if (d < closestDist) {
							ia = i;
							ib = j;
							closestDist = d;
						}
					}
				}
			}
		}

		public static bool MergeContours (NativeList<int> verts, ref VoxelContour ca, ref VoxelContour cb, int ia, int ib) {
			// Note: this will essentially leave junk data in the verts array where the contours were previously.
			// This shouldn't be a big problem because MergeContours is normally not called for that many contours (usually none).
			int nv = 0;
			var startIndex = verts.Length;

			// Copy contour A.
			for (int i = 0; i <= ca.nverts; i++) {
				int src = ca.vertexStartIndex + ((ia+i) % ca.nverts)*4;
				verts.Add(verts[src+0]);
				verts.Add(verts[src+1]);
				verts.Add(verts[src+2]);
				verts.Add(verts[src+3]);
				nv++;
			}

			// Copy contour B
			for (int i = 0; i <= cb.nverts; i++) {
				int src = cb.vertexStartIndex + ((ib+i) % cb.nverts)*4;
				verts.Add(verts[src+0]);
				verts.Add(verts[src+1]);
				verts.Add(verts[src+2]);
				verts.Add(verts[src+3]);
				nv++;
			}

			ca.vertexStartIndex = startIndex;
			ca.nverts = nv;

			cb.vertexStartIndex = 0;
			cb.nverts = 0;

			return true;
		}

		public void SimplifyContour (NativeList<int> verts, NativeList<int> simplified, float maxError, int buildFlags) {
			// Add initial points.
			bool hasConnections = false;

			for (int i = 0; i < verts.Length; i += 4) {
				if ((verts[i+3] & VoxelUtilityBurst.ContourRegMask) != 0) {
					hasConnections = true;
					break;
				}
			}

			if (hasConnections) {
				// The contour has some portals to other regions.
				// Add a new point to every location where the region changes.
				for (int i = 0, ni = verts.Length/4; i < ni; i++) {
					int ii = (i+1) % ni;
					bool differentRegs = (verts[i*4+3] & VoxelUtilityBurst.ContourRegMask) != (verts[ii*4+3] & VoxelUtilityBurst.ContourRegMask);
					bool areaBorders = (verts[i*4+3] & VoxelUtilityBurst.RC_AREA_BORDER) != (verts[ii*4+3] & VoxelUtilityBurst.RC_AREA_BORDER);

					if (differentRegs || areaBorders) {
						simplified.Add(verts[i*4+0]);
						simplified.Add(verts[i*4+1]);
						simplified.Add(verts[i*4+2]);
						simplified.Add(i);
					}
				}
			}


			if (simplified.Length == 0) {
				// If there is no connections at all,
				// create some initial points for the simplification process.
				// Find lower-left and upper-right vertices of the contour.
				int llx = verts[0];
				int lly = verts[1];
				int llz = verts[2];
				int lli = 0;
				int urx = verts[0];
				int ury = verts[1];
				int urz = verts[2];
				int uri = 0;

				for (int i = 0; i < verts.Length; i += 4) {
					int x = verts[i+0];
					int y = verts[i+1];
					int z = verts[i+2];
					if (x < llx || (x == llx && z < llz)) {
						llx = x;
						lly = y;
						llz = z;
						lli = i/4;
					}
					if (x > urx || (x == urx && z > urz)) {
						urx = x;
						ury = y;
						urz = z;
						uri = i/4;
					}
				}

				simplified.Add(llx);
				simplified.Add(lly);
				simplified.Add(llz);
				simplified.Add(lli);

				simplified.Add(urx);
				simplified.Add(ury);
				simplified.Add(urz);
				simplified.Add(uri);
			}

			// Add points until all raw points are within
			// error tolerance to the simplified shape.
			// This uses the Douglas-Peucker algorithm.
			int pn = verts.Length/4;

			//Use the max squared error instead
			maxError *= maxError;

			for (int i = 0; i < simplified.Length/4;) {
				int ii = (i+1) % (simplified.Length/4);

				int ax = simplified[i*4+0];
				int az = simplified[i*4+2];
				int ai = simplified[i*4+3];

				int bx = simplified[ii*4+0];
				int bz = simplified[ii*4+2];
				int bi = simplified[ii*4+3];

				// Find maximum deviation from the segment.
				float maxd = 0;
				int maxi = -1;
				int ci, cinc, endi;

				// Traverse the segment in lexilogical order so that the
				// max deviation is calculated similarly when traversing
				// opposite segments.
				if (bx > ax || (bx == ax && bz > az)) {
					cinc = 1;
					ci = (ai+cinc) % pn;
					endi = bi;
				} else {
					cinc = pn-1;
					ci = (bi+cinc) % pn;
					endi = ai;
					Util.Memory.Swap(ref ax, ref bx);
					Util.Memory.Swap(ref az, ref bz);
				}

				// Tessellate only outer edges or edges between areas.
				if ((verts[ci*4+3] & VoxelUtilityBurst.ContourRegMask) == 0 ||
					(verts[ci*4+3] & VoxelUtilityBurst.RC_AREA_BORDER) == VoxelUtilityBurst.RC_AREA_BORDER) {
					while (ci != endi) {
						float d2 = VectorMath.SqrDistancePointSegmentApproximate(verts[ci*4+0], verts[ci*4+2]/field.width, ax, az/field.width, bx, bz/field.width);

						if (d2 > maxd) {
							maxd = d2;
							maxi = ci;
						}
						ci = (ci+cinc) % pn;
					}
				}

				// If the max deviation is larger than accepted error,
				// add new point, else continue to next segment.
				if (maxi != -1 && maxd > maxError) {
					// Add space for the new point.
					//simplified.resize(simplified.size()+4);
					simplified.Add(0);
					simplified.Add(0);
					simplified.Add(0);
					simplified.Add(0);

					int n = simplified.Length/4;

					for (int j = n-1; j > i; --j) {
						simplified[j*4+0] = simplified[(j-1)*4+0];
						simplified[j*4+1] = simplified[(j-1)*4+1];
						simplified[j*4+2] = simplified[(j-1)*4+2];
						simplified[j*4+3] = simplified[(j-1)*4+3];
					}
					// Add the point.
					simplified[(i+1)*4+0] = verts[maxi*4+0];
					simplified[(i+1)*4+1] = verts[maxi*4+1];
					simplified[(i+1)*4+2] = verts[maxi*4+2];
					simplified[(i+1)*4+3] = maxi;
				} else {
					i++;
				}
			}



			//Split too long edges

			float maxEdgeLen = maxEdgeLength / cellSize;

			if (maxEdgeLen > 0 && (buildFlags & (VoxelUtilityBurst.RC_CONTOUR_TESS_WALL_EDGES|VoxelUtilityBurst.RC_CONTOUR_TESS_AREA_EDGES|VoxelUtilityBurst.RC_CONTOUR_TESS_TILE_EDGES)) != 0) {
				for (int i = 0; i < simplified.Length/4;) {
					if (simplified.Length/4 > 200) {
						break;
					}

					int ii = (i+1) % (simplified.Length/4);

					int ax = simplified[i*4+0];
					int az = simplified[i*4+2];
					int ai = simplified[i*4+3];

					int bx = simplified[ii*4+0];
					int bz = simplified[ii*4+2];
					int bi = simplified[ii*4+3];

					// Find maximum deviation from the segment.
					int maxi = -1;
					int ci = (ai+1) % pn;

					// Tessellate only outer edges or edges between areas.
					bool tess = false;

					// Wall edges.
					if ((buildFlags & VoxelUtilityBurst.RC_CONTOUR_TESS_WALL_EDGES) != 0 && (verts[ci*4+3] & VoxelUtilityBurst.ContourRegMask) == 0)
						tess = true;

					// Edges between areas.
					if ((buildFlags & VoxelUtilityBurst.RC_CONTOUR_TESS_AREA_EDGES) != 0 && (verts[ci*4+3] & VoxelUtilityBurst.RC_AREA_BORDER) == VoxelUtilityBurst.RC_AREA_BORDER)
						tess = true;

					// Border of tile
					if ((buildFlags & VoxelUtilityBurst.RC_CONTOUR_TESS_TILE_EDGES) != 0 && (verts[ci*4+3] & VoxelUtilityBurst.BorderReg) == VoxelUtilityBurst.BorderReg)
						tess = true;

					if (tess) {
						int dx = bx - ax;
						int dz = (bz/field.width) - (az/field.width);
						if (dx*dx + dz*dz > maxEdgeLen*maxEdgeLen) {
							// Round based on the segments in lexilogical order so that the
							// max tesselation is consistent regardles in which direction
							// segments are traversed.
							int n = bi < ai ? (bi+pn - ai) : (bi - ai);
							if (n > 1) {
								if (bx > ax || (bx == ax && bz > az)) {
									maxi = (ai + n/2) % pn;
								} else {
									maxi = (ai + (n+1)/2) % pn;
								}
							}
						}
					}

					// If the max deviation is larger than accepted error,
					// add new point, else continue to next segment.
					if (maxi != -1) {
						// Add space for the new point.
						//simplified.resize(simplified.size()+4);
						simplified.Resize(simplified.Length + 4, NativeArrayOptions.UninitializedMemory);

						int n = simplified.Length/4;
						for (int j = n-1; j > i; --j) {
							simplified[j*4+0] = simplified[(j-1)*4+0];
							simplified[j*4+1] = simplified[(j-1)*4+1];
							simplified[j*4+2] = simplified[(j-1)*4+2];
							simplified[j*4+3] = simplified[(j-1)*4+3];
						}
						// Add the point.
						simplified[(i+1)*4+0] = verts[maxi*4+0];
						simplified[(i+1)*4+1] = verts[maxi*4+1];
						simplified[(i+1)*4+2] = verts[maxi*4+2];
						simplified[(i+1)*4+3] = maxi;
					} else {
						++i;
					}
				}
			}

			for (int i = 0; i < simplified.Length/4; i++) {
				// The edge vertex flag is take from the current raw point,
				// and the neighbour region is take from the next raw point.
				int ai = (simplified[i*4+3]+1) % pn;
				int bi = simplified[i*4+3];
				simplified[i*4+3] = (verts[ai*4+3] & VoxelUtilityBurst.ContourRegMask) | (verts[bi*4+3] & VoxelUtilityBurst.RC_BORDER_VERTEX);
			}
		}

		public void WalkContour (int x, int z, int i, NativeArray<ushort> flags, NativeList<int> verts) {
			// Choose the first non-connected edge
			int dir = 0;

			while ((flags[i] & (ushort)(1 << dir)) == 0) {
				dir++;
			}

			int startDir = dir;
			int startI = i;

			int area = field.areaTypes[i];

			int iter = 0;


			while (iter++ < 40000) {
				//Are we facing a region edge
				if ((flags[i] & (ushort)(1 << dir)) != 0) {
					//Choose the edge corner
					bool isBorderVertex = false;
					bool isAreaBorder = false;

					int px = x;
					int py = GetCornerHeight(x, z, i, dir, ref isBorderVertex);
					int pz = z;

					switch (dir) {
					case 0: pz += field.width;; break;
					case 1: px++; pz += field.width; break;
					case 2: px++; break;
					}

					/*case 1: px++; break;
					 *  case 2: px++; pz += field.width; break;
					 *  case 3: pz += field.width; break;
					 */

					int r = 0;
					CompactVoxelSpan s = field.spans[i];

					if (s.GetConnection(dir) != CompactVoxelField.NotConnected) {
						int ni = (int)field.cells[field.GetNeighbourIndex(x+z, dir)].index + s.GetConnection(dir);
						r = (int)field.spans[ni].reg;

						if (area != field.areaTypes[ni]) {
							isAreaBorder = true;
						}
					}

					if (isBorderVertex) {
						r |= VoxelUtilityBurst.RC_BORDER_VERTEX;
					}
					if (isAreaBorder) {
						r |= VoxelUtilityBurst.RC_AREA_BORDER;
					}

					verts.Add(px);
					verts.Add(py);
					verts.Add(pz);
					verts.Add(r);

					//Debug.DrawRay (previousPos,new Vector3 ((dir == 1 || dir == 2) ? 1 : 0, 0, (dir == 0 || dir == 1) ? 1 : 0),Color.cyan);

					flags[i] = (ushort)(flags[i] & ~(1 << dir)); // Remove visited edges

					// & 0x3 is the same as % 4 (for positive numbers)
					dir = (dir+1) & 0x3;  // Rotate CW
				} else {
					int ni = -1;
					int nx = x + VoxelUtilityBurst.DX[dir];
					int nz = z + VoxelUtilityBurst.DZ[dir]*field.width;

					CompactVoxelSpan s = field.spans[i];

					if (s.GetConnection(dir) != CompactVoxelField.NotConnected) {
						CompactVoxelCell nc = field.cells[nx+nz];
						ni = (int)nc.index + s.GetConnection(dir);
					}

					if (ni == -1) {
						Debug.LogWarning("Degenerate triangles might have been generated.\n" +
							"Usually this is not a problem, but if you have a static level, try to modify the graph settings slightly to avoid this edge case.");
						return;
					}
					x = nx;
					z = nz;
					i = ni;

					// & 0x3 is the same as % 4 (modulo 4)
					dir = (dir+3) & 0x3;    // Rotate CCW
				}

				if (startI == i && startDir == dir) {
					break;
				}
			}
		}

		public int GetCornerHeight (int x, int z, int i, int dir, ref bool isBorderVertex) {
			CompactVoxelSpan s = field.spans[i];

			int ch = (int)s.y;

			//dir + clockwise direction
			int dirp = (dir+1) & 0x3;

			//int dirp = (dir+3) & 0x3;

			// TODO: Optimize
			var regs = new NativeArray<uint>(4, Allocator.Temp);

			regs[0] = (uint)field.spans[i].reg | ((uint)field.areaTypes[i] << 16);

			if (s.GetConnection(dir) != CompactVoxelField.NotConnected) {
				int neighbourCell = field.GetNeighbourIndex(x+z, dir);
				int ni = (int)field.cells[neighbourCell].index + s.GetConnection(dir);

				CompactVoxelSpan ns = field.spans[ni];

				ch = System.Math.Max(ch, (int)ns.y);
				regs[1] = (uint)ns.reg | ((uint)field.areaTypes[ni] << 16);

				if (ns.GetConnection(dirp) != CompactVoxelField.NotConnected) {
					int neighbourCell2 = field.GetNeighbourIndex(neighbourCell, dirp);
					int ni2 = (int)field.cells[neighbourCell2].index + ns.GetConnection(dirp);

					CompactVoxelSpan ns2 = field.spans[ni2];

					ch = System.Math.Max(ch, (int)ns2.y);
					regs[2] = (uint)ns2.reg | ((uint)field.areaTypes[ni2] << 16);
				}
			}

			if (s.GetConnection(dirp) != CompactVoxelField.NotConnected) {
				int neighbourCell = field.GetNeighbourIndex(x+z, dirp);
				int ni = (int)field.cells[neighbourCell].index + s.GetConnection(dirp);

				CompactVoxelSpan ns = field.spans[ni];

				ch = System.Math.Max(ch, (int)ns.y);
				regs[3] = (uint)ns.reg | ((uint)field.areaTypes[ni] << 16);

				if (ns.GetConnection(dir) != CompactVoxelField.NotConnected) {
					int neighbourCell2 = field.GetNeighbourIndex(neighbourCell, dir);
					int ni2 = (int)field.cells[neighbourCell2].index + ns.GetConnection(dir);

					CompactVoxelSpan ns2 = field.spans[ni2];

					ch = System.Math.Max(ch, (int)ns2.y);
					regs[2] = (uint)ns2.reg | ((uint)field.areaTypes[ni2] << 16);
				}
			}

			// Check if the vertex is special edge vertex, these vertices will be removed later.
			for (int j = 0; j < 4; ++j) {
				int a = j;
				int b = (j+1) & 0x3;
				int c = (j+2) & 0x3;
				int d = (j+3) & 0x3;

				// The vertex is a border vertex there are two same exterior cells in a row,
				// followed by two interior cells and none of the regions are out of bounds.
				bool twoSameExts = (regs[a] & regs[b] & VoxelUtilityBurst.BorderReg) != 0 && regs[a] == regs[b];
				bool twoInts = ((regs[c] | regs[d]) & VoxelUtilityBurst.BorderReg) == 0;
				bool intsSameArea = (regs[c]>>16) == (regs[d]>>16);
				bool noZeros = regs[a] != 0 && regs[b] != 0 && regs[c] != 0 && regs[d] != 0;
				if (twoSameExts && twoInts && intsSameArea && noZeros) {
					isBorderVertex = true;
					break;
				}
			}

			return ch;
		}

		static void RemoveRange (NativeList<int> arr, int index, int count) {
			for (int i = index; i < arr.Length - count; i++) {
				arr[i] = arr[i+count];
			}
			arr.Resize(arr.Length - count, NativeArrayOptions.UninitializedMemory);
		}

		static void RemoveDegenerateSegments (NativeList<int> simplified) {
			// Remove adjacent vertices which are equal on xz-plane,
			// or else the triangulator will get confused
			for (int i = 0; i < simplified.Length/4; i++) {
				int ni = i+1;
				if (ni >= (simplified.Length/4))
					ni = 0;

				if (simplified[i*4+0] == simplified[ni*4+0] &&
					simplified[i*4+2] == simplified[ni*4+2]) {
					// Degenerate segment, remove.
					RemoveRange(simplified, i, 4);
				}
			}
		}

		int CalcAreaOfPolygon2D (NativeArray<int> verts, int vertexStartIndex, int nverts) {
			int area = 0;

			for (int i = 0, j = nverts-1; i < nverts; j = i++) {
				int vi = vertexStartIndex + i*4;
				int vj = vertexStartIndex + j*4;
				area += verts[vi+0] * (verts[vj+2]/field.width) - verts[vj+0] * (verts[vi+2]/field.width);
			}

			return (area+1) / 2;
		}

		static bool Ileft (NativeArray<int> verts, int a, int b, int c) {
			return (verts[b+0] - verts[a+0]) * (verts[c+2] - verts[a+2]) - (verts[c+0] - verts[a+0]) * (verts[b+2] - verts[a+2]) <= 0;
		}
	}
}
