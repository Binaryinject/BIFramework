using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

namespace Pathfinding.Voxels.Burst {
	public unsafe struct UnsafeSpan<T> where T : unmanaged {
		[NativeDisableUnsafePtrRestriction]
		T* data;
		int length;

		public UnsafeSpan(NativeSlice<T> arr) {
			data = (T*)arr.GetUnsafePtr();
			length = arr.Length;
		}

		public int Length => length;

		public T this[int index] {
			get {
				return *(data + index);
			}
			set {
				*(data + index) = value;
			}
		}
	}

	public struct RasterizationMesh {
		public UnsafeSpan<float3> vertices;

		public UnsafeSpan<int> triangles;

		/// <summary>If true, the <see cref="area"/> will be interpreted as a node tag and applied to the final nodes</summary>
		public bool areaIsTag;

		public int area;

		/// <summary>World bounds of the mesh. Assumed to already be multiplied with the matrix</summary>
		public Bounds bounds;

		public Matrix4x4 matrix;

		/// <summary>
		/// If true then the mesh will be treated as solid and its interior will be unwalkable.
		/// The unwalkable region will be the minimum to maximum y coordinate in each cell.
		/// </summary>
		public bool solid;
	}

	[BurstCompile(CompileSynchronously = true)]
	public struct JobVoxelize : IJob {
		[ReadOnly]
		public NativeArray<RasterizationMesh> inputMeshes;

		[ReadOnly]
		public NativeArray<int> bucket;

		/// <summary>Maximum ledge height that is considered to still be traversable. [Limit: >=0] [Units: vx]</summary>
		public int voxelWalkableClimb;

		/// <summary>
		/// Minimum floor to 'ceiling' height that will still allow the floor area to
		/// be considered walkable. [Limit: >= 3] [Units: vx]
		/// </summary>
		public uint voxelWalkableHeight;

		/// <summary>The xz-plane cell size to use for fields. [Limit: > 0] [Units: wu]</summary>
		public float cellSize;

		/// <summary>The y-axis cell size to use for fields. [Limit: > 0] [Units: wu]</summary>
		public float cellHeight;

		/// <summary>The maximum slope that is considered walkable. [Limits: 0 <= value < 90] [Units: Degrees]</summary>
		public float maxSlope;

		public Matrix4x4 graphTransform;
		public Bounds graphSpaceBounds;
		public LinkedVoxelField voxelArea;

		public void Execute () {
			voxelArea.ResetLinkedVoxelSpans();

			// Transform from voxel space to graph space.
			// then scale from voxel space (one unit equals one voxel)
			// Finally add min
			Matrix4x4 voxelMatrix = Matrix4x4.TRS(graphSpaceBounds.min, Quaternion.identity, Vector3.one) * Matrix4x4.Scale(new Vector3(cellSize, cellHeight, cellSize));

			// Transform from voxel space to world space
			// add half a voxel to fix rounding
			var transform = graphTransform * voxelMatrix * Matrix4x4.Translate(new Vector3(0.5f, 0, 0.5f));
			var world2voxelMatrix = transform.inverse;

			int maximumVoxelYCoord = (int)(graphSpaceBounds.size.y / cellHeight);

			// Cosine of the slope limit in voxel space (some tweaks are needed because the voxel space might be stretched out along the y axis)
			float slopeLimit = math.cos(math.atan((cellSize/cellHeight)*math.tan(maxSlope*Mathf.Deg2Rad)));

			// Temporary arrays used for rasterization
			var clipperOrig = new VoxelPolygonClipperBurst();
			var clipperX1 = new VoxelPolygonClipperBurst();
			var clipperX2 = new VoxelPolygonClipperBurst();
			var clipperZ1 = new VoxelPolygonClipperBurst();
			var clipperZ2 = new VoxelPolygonClipperBurst();

			// Find the largest lengths of vertex arrays and check for meshes which can be skipped
			int maxVerts = 0;
			for (int m = 0; m < bucket.Length; m++) {
				maxVerts = math.max(inputMeshes[bucket[m]].vertices.Length, maxVerts);
			}

			// Create buffer, here vertices will be stored multiplied with the local-to-voxel-space matrix
			var verts = new NativeArray<Vector3>(maxVerts, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			int width = voxelArea.width;
			int depth = voxelArea.depth;

			// This loop is the hottest place in the whole rasterization process
			// it usually accounts for around 50% of the time
			for (int m = 0; m < bucket.Length; m++) {
				RasterizationMesh mesh = inputMeshes[bucket[m]];
				var meshMatrix = mesh.matrix;

				// Flip the orientation of all faces if the mesh is scaled in such a way
				// that the face orientations would change
				// This happens for example if a mesh has a negative scale along an odd number of axes
				// e.g it happens for the scale (-1, 1, 1) but not for (-1, -1, 1) or (1,1,1)
				var flipOrientation = VectorMath.ReversesFaceOrientations(meshMatrix);

				var vs = mesh.vertices;
				var tris = mesh.triangles;

				// Transform vertices first to world space and then to voxel space
				var localToVoxelMatrix = world2voxelMatrix * mesh.matrix;
				for (int i = 0; i < vs.Length; i++) verts[i] = localToVoxelMatrix.MultiplyPoint3x4(vs[i]);

				int mesharea = mesh.area;
				if (mesh.areaIsTag) {
					mesharea |= VoxelUtilityBurst.TagReg;
				}

				var meshBounds = new IntRect();

				for (int i = 0; i < tris.Length; i += 3) {
					Vector3 p1 = verts[tris[i]];
					Vector3 p2 = verts[tris[i+1]];
					Vector3 p3 = verts[tris[i+2]];

					if (flipOrientation) {
						var tmp = p1;
						p1 = p3;
						p3 = tmp;
					}

					int minX = (int)(Utility.Min(p1.x, p2.x, p3.x));
					int minZ = (int)(Utility.Min(p1.z, p2.z, p3.z));

					int maxX = (int)math.ceil(Utility.Max(p1.x, p2.x, p3.x));
					int maxZ = (int)math.ceil(Utility.Max(p1.z, p2.z, p3.z));

					minX = math.clamp(minX, 0, width-1);
					maxX = math.clamp(maxX, 0, width-1);
					minZ = math.clamp(minZ, 0, depth-1);
					maxZ = math.clamp(maxZ, 0, depth-1);

					// Check if the mesh is completely out of bounds
					if (minX >= width || minZ >= depth || maxX <= 0 || maxZ <= 0) continue;

					if (i == 0) meshBounds = new IntRect(minX, minZ, minX, minZ);
					meshBounds.xmin = math.min(meshBounds.xmin, minX);
					meshBounds.xmax = math.max(meshBounds.xmax, maxX);
					meshBounds.ymin = math.min(meshBounds.ymin, minZ);
					meshBounds.ymax = math.max(meshBounds.ymax, maxZ);

					// Check max slope
					Vector3 normal = Vector3.Cross(p2-p1, p3-p1);
					float cosSlopeAngle = Vector3.Dot(normal.normalized, Vector3.up);
					int area = cosSlopeAngle < slopeLimit ? CompactVoxelField.UnwalkableArea : 1 + mesharea;

					clipperOrig[0] = p1;
					clipperOrig[1] = p2;
					clipperOrig[2] = p3;
					clipperOrig.n = 3;

					for (int x = minX; x <= maxX; x++) {
						clipperOrig.ClipPolygonAlongX(ref clipperX1, 1f, -x+0.5f);

						if (clipperX1.n < 3) {
							continue;
						}

						clipperX1.ClipPolygonAlongX(ref clipperX2, -1F, x+0.5F);

						if (clipperX2.n < 3) {
							continue;
						}

						float clampZ1, clampZ2;
						unsafe {
							clampZ1 = clampZ2 = clipperX2.z[0];
							for (int q = 1; q < clipperX2.n; q++) {
								float val = clipperX2.z[q];
								clampZ1 = math.min(clampZ1, val);
								clampZ2 = math.max(clampZ2, val);
							}
						}

						int clampZ1I = math.clamp((int)math.round(clampZ1), 0, depth-1);
						int clampZ2I = math.clamp((int)math.round(clampZ2), 0, depth-1);

						for (int z = clampZ1I; z <= clampZ2I; z++) {
							clipperX2.ClipPolygonAlongZWithYZ(ref clipperZ1, 1F, -z+0.5F);

							if (clipperZ1.n < 3) {
								continue;
							}

							clipperZ1.ClipPolygonAlongZWithY(ref clipperZ2, -1F, z+0.5F);
							if (clipperZ2.n < 3) {
								continue;
							}

							float sMin, sMax;
							unsafe {
								sMin = sMax = clipperZ2.y[0];
								for (int q = 1; q < clipperZ2.n; q++) {
									float val = clipperZ2.y[q];
									sMin = math.min(sMin, val);
									sMax = math.max(sMax, val);
								}
							}

							int maxi = (int)math.ceil(sMax);

							// Skip span if below or above the bounding box
							if (maxi >= 0 && sMin <= maximumVoxelYCoord) {
								// Make sure mini >= 0
								int mini = math.max(0, (int)sMin);
								// Make sure the span is at least 1 voxel high
								maxi = math.max(mini+1, maxi);

								voxelArea.AddLinkedSpan(z*width+x, (uint)mini, (uint)maxi, area, voxelWalkableClimb, m);
							}
						}
					}
				}

				if (mesh.solid) {
					for (int z = meshBounds.ymin; z <= meshBounds.ymax; z++) {
						for (int x = meshBounds.xmin; x <= meshBounds.xmax; x++) {
							voxelArea.ResolveSolid(z*voxelArea.width + x, m, voxelWalkableClimb);
						}
					}
				}
			}
		}
	}

	[BurstCompile(CompileSynchronously = true)]
	struct JobBuildCompactField : IJob {
		public LinkedVoxelField input;
		public CompactVoxelField output;

		public void Execute () {
			output.BuildFromLinkedField(input);
		}
	}


	[BurstCompile(CompileSynchronously = true)]
	struct JobBuildConnections : IJob {
		public CompactVoxelField field;
		public int voxelWalkableHeight;
		public int voxelWalkableClimb;

		public void Execute () {
			int wd = field.width*field.depth;

			// Build voxel connections
			for (int z = 0, pz = 0; z < wd; z += field.width, pz++) {
				for (int x = 0; x < field.width; x++) {
					CompactVoxelCell c = field.cells[x+z];

					for (int i = (int)c.index, ni = (int)(c.index+c.count); i < ni; i++) {
						CompactVoxelSpan s = field.spans[i];
						s.con = 0xFFFFFFFF;

						for (int d = 0; d < 4; d++) {
							int nx = x+VoxelUtilityBurst.DX[d];
							int nz = z+VoxelUtilityBurst.DZ[d]*field.width;

							if (nx < 0 || nz < 0 || nz >= wd || nx >= field.width) {
								continue;
							}

							CompactVoxelCell nc = field.cells[nx+nz];

							for (int k = nc.index, nk = (int)(nc.index+nc.count); k < nk; k++) {
								CompactVoxelSpan ns = field.spans[k];

								int bottom = System.Math.Max(s.y, ns.y);

								int top = System.Math.Min((int)s.y+(int)s.h, (int)ns.y+(int)ns.h);

								if ((top-bottom) >= voxelWalkableHeight && System.Math.Abs((int)ns.y - (int)s.y) <= voxelWalkableClimb) {
									uint connIdx = (uint)k - (uint)nc.index;

									if (connIdx > CompactVoxelField.MaxLayers) {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
										throw new System.Exception("Too many layers");
#else
										break;
#endif
									}

									s.SetConnection(d, connIdx);
									break;
								}
							}
						}

						field.spans[i] = s;
					}
				}
			}
		}
	}

	[BurstCompile(CompileSynchronously = true)]
	struct JobErodeWalkableArea : IJob {
		public CompactVoxelField field;
		public int radius;

		public void Execute () {
			var distances = new NativeArray<ushort>(field.spans.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			VoxelUtilityBurst.CalculateDistanceField(field, distances);

			for (int i = 0; i < distances.Length; i++) {
				// Note multiplied with 2 because the distance field increments distance by 2 for each voxel (and 3 for diagonal)
				if (distances[i] < radius*2) {
					field.areaTypes[i] = CompactVoxelField.UnwalkableArea;
				}
			}
		}
	}

	[BurstCompile(CompileSynchronously = true)]
	struct JobBuildDistanceField : IJob {
		public CompactVoxelField field;
		public NativeList<ushort> output;

		public void Execute () {
			var distances = new NativeArray<ushort>(field.spans.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			VoxelUtilityBurst.CalculateDistanceField(field, distances);

			output.ResizeUninitialized(field.spans.Length);
			VoxelUtilityBurst.BoxBlur(field, distances, output);
		}
	}

	[BurstCompile(CompileSynchronously = true)]
	struct JobFilterLowHeightSpans : IJob {
		public LinkedVoxelField field;
		public uint voxelWalkableHeight;

		public void Execute () {
			int wd = field.width*field.depth;
			//Filter all ledges
			var spans = field.linkedSpans;

			for (int z = 0, pz = 0; z < wd; z += field.width, pz++) {
				for (int x = 0; x < field.width; x++) {
					for (int s = z+x; s != -1 && spans[s].bottom != LinkedVoxelField.InvalidSpanValue; s = spans[s].next) {
						uint bottom = spans[s].top;
						uint top = spans[s].next != -1 ? spans[spans[s].next].bottom : LinkedVoxelField.MaxHeight;

						if (top - bottom < voxelWalkableHeight) {
							var span = spans[s];
							span.area = CompactVoxelField.UnwalkableArea;
							spans[s] = span;
						}
					}
				}
			}
		}
	}

	[BurstCompile(CompileSynchronously = true)]
	struct JobFilterLedges : IJob {
		public LinkedVoxelField field;
		public uint voxelWalkableHeight;
		public int voxelWalkableClimb;
		public float cellSize;
		public float cellHeight;

		// Code almost completely ripped from Recast
		public void Execute () {
			var spans = field.linkedSpans;
			int wd = field.width*field.depth;
			int width = field.width;

			// Filter all ledges
			for (int z = 0, pz = 0; z < wd; z += width, pz++) {
				for (int x = 0; x < width; x++) {
					if (spans[x+z].bottom == LinkedVoxelField.InvalidSpanValue) continue;

					for (int s = x+z; s != -1; s = spans[s].next) {
						// Skip non-walkable spans
						if (spans[s].area == CompactVoxelField.UnwalkableArea) {
							continue;
						}

						int bottom = (int)spans[s].top;
						int top = spans[s].next != -1 ? (int)spans[spans[s].next].bottom : (int)LinkedVoxelField.MaxHeight;

						int minHeight =  (int)LinkedVoxelField.MaxHeight;

						int aMinHeight = (int)spans[s].top;
						int aMaxHeight = aMinHeight;

						for (int d = 0; d < 4; d++) {
							int nx = x + VoxelUtilityBurst.DX[d];
							int nz = z + VoxelUtilityBurst.DZ[d]*width;

							// Skip out-of-bounds points
							if (nx < 0 || nz < 0 || nz >= wd || nx >= width) {
								var span = spans[s];
								span.area = CompactVoxelField.UnwalkableArea;
								spans[s] = span;
								break;
							}

							int nsx = nx+nz;

							int nbottom = -voxelWalkableClimb;

							int ntop = spans[nsx].bottom != LinkedVoxelField.InvalidSpanValue ? (int)spans[nsx].bottom : (int)LinkedVoxelField.MaxHeight;

							if (math.min(top, ntop) - math.max(bottom, nbottom) > voxelWalkableHeight) {
								minHeight = math.min(minHeight, nbottom - bottom);
							}

							// Loop through spans
							if (spans[nsx].bottom != LinkedVoxelField.InvalidSpanValue) {
								for (int ns = nsx; ns != -1; ns = spans[ns].next) {
									nbottom = (int)spans[ns].top;
									ntop = spans[ns].next != -1 ? (int)spans[spans[ns].next].bottom : (int)LinkedVoxelField.MaxHeight;

									if (math.min(top, ntop) - math.max(bottom, nbottom) > voxelWalkableHeight) {
										minHeight = math.min(minHeight, nbottom - bottom);

										if (math.abs(nbottom - bottom) <= voxelWalkableClimb) {
											if (nbottom < aMinHeight) { aMinHeight = nbottom; }
											if (nbottom > aMaxHeight) { aMaxHeight = nbottom; }
										}
									}
								}
							}
						}

						if (minHeight < -voxelWalkableClimb || (aMaxHeight - aMinHeight) > voxelWalkableClimb) {
							var span = spans[s];
							span.area = CompactVoxelField.UnwalkableArea;
							spans[s] = span;
						}
					}
				}
			}
		}
	}
}
