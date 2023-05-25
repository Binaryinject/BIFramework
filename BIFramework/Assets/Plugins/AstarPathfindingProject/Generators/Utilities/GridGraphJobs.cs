using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pathfinding.Jobs.Grid {
	// Using BurstCompile to compile a Job with Burst
	// Set CompileSynchronously to true to make sure that the method will not be compiled asynchronously
	// but on the first schedule
	[BurstCompile]
	public struct JobPrepareGridRaycast : IJob {
		public Matrix4x4 graphToWorld;
		public IntBounds bounds;
		public Vector3 raycastOffset;
		public Vector3 raycastDirection;
		public LayerMask raycastMask;

		[WriteOnly]
		public NativeArray<RaycastCommand> raycastCommands;

		public void Execute () {
			float raycastLength = raycastDirection.magnitude;
			var size = bounds.size;

#if UNITY_2022_2_OR_NEWER
			var queryParameters = new QueryParameters(raycastMask, false, QueryTriggerInteraction.Ignore, false);
#else
			const int RaycastMaxHits = 1;
#endif
			// In particular Unity 2022.2 seems to assert that RaycastCommands use normalized directions
			var direction = raycastDirection.normalized;

			for (int z = 0; z < size.z; z++) {
				int zw = z * size.x;
				for (int x = 0; x < size.x; x++) {
					int idx = zw + x;
					var pos = graphToWorld.MultiplyPoint3x4(new Vector3((bounds.min.x + x) + 0.5f, 0, (bounds.min.z + z) + 0.5f));
#if UNITY_2022_2_OR_NEWER
					raycastCommands[idx] = new RaycastCommand(pos + raycastOffset, direction, queryParameters, raycastLength);
#else
					raycastCommands[idx] = new RaycastCommand(pos + raycastOffset, direction, raycastLength, raycastMask, RaycastMaxHits);
#endif
				}
			}
		}
	}

	/// <summary>result[i] = neither hit1[i] nor hit2[i] hit anything</summary>
	[BurstCompile]
	public struct JobMergeRaycastCollisionHits : IJob {
		[ReadOnly]
		public NativeArray<RaycastHit> hit1;

		[ReadOnly]
		public NativeArray<RaycastHit> hit2;

		[WriteOnly]
		public NativeArray<bool> result;

		public void Execute () {
			for (int i = 0; i < hit1.Length; i++) {
				result[i] = hit1[i].normal == Vector3.zero && hit2[i].normal == Vector3.zero;
			}
		}
	}

	[BurstCompile]
	public struct JobPrepareRaycasts : IJob {
		public Vector3 direction;
		public Vector3 originOffset;
		public float distance;
		public LayerMask mask;

		[ReadOnly]
		public NativeArray<Vector3> origins;

		[WriteOnly]
		public NativeArray<RaycastCommand> raycastCommands;

		public void Execute () {
#if UNITY_2022_2_OR_NEWER
			var queryParameters = new QueryParameters(mask, false, QueryTriggerInteraction.Ignore, false);
#endif
			// In particular Unity 2022.2 seems to assert that RaycastCommands use normalized directions
			var direction = this.direction.normalized;

			for (int i = 0; i < raycastCommands.Length; i++) {
#if UNITY_2022_2_OR_NEWER
				raycastCommands[i] = new RaycastCommand(origins[i] + originOffset, direction, queryParameters, distance);
#else
				raycastCommands[i] = new RaycastCommand(origins[i] + originOffset, direction, distance, mask, 1);
#endif
			}
		}
	}

	[BurstCompile]
	public struct JobNodePositions : IJob {
		public Matrix4x4 graphToWorld;
		public IntBounds bounds;

		[WriteOnly]
		public NativeArray<Vector3> nodePositions;

		public static Vector3 NodePosition (Matrix4x4 graphToWorld, IntBounds bounds, int dataX, int dataZ) {
			return graphToWorld.MultiplyPoint3x4(new Vector3((bounds.min.x + dataX) + 0.5f, 0, (bounds.min.z + dataZ) + 0.5f));
		}

		public void Execute () {
			var size = bounds.size;
			int i = 0;

			for (int y = 0; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					for (int x = 0; x < size.x; x++, i++) {
						nodePositions[i] = NodePosition(graphToWorld, bounds, x, z);
					}
				}
			}
		}
	}

	[BurstCompile(FloatMode = FloatMode.Fast)]
	public struct JobNodeWalkable : IJob {
		public bool useRaycastNormal;
		public float maxSlope;
		public Vector3 up;
		public bool unwalkableWhenNoGround;
		public float characterHeight;
		public int layerStride;

		[ReadOnly]
		public NativeArray<float3> nodePositions;

		[ReadOnly]
		public NativeArray<float4> nodeNormals;

		[WriteOnly]
		public NativeArray<bool> nodeWalkable;

		bool DidHit (RaycastHit hit) {
			return hit.normal != Vector3.zero;
		}

		public void Execute () {
			// Cosinus of the max slope
			float cosMaxSlopeAngle = math.cos(math.radians(maxSlope));
			float4 upNative = new float4(up.x, up.y, up.z, 0);
			float3 upNative3 = upNative.xyz;

			for (int i = 0; i < nodeNormals.Length; i++) {
				// walkable will be set to false if no ground was found (unless that setting has been disabled)
				// The normal will only be non-zero if something was hit.
				bool didHit = math.any(nodeNormals[i]);
				var walkable = didHit || (!unwalkableWhenNoGround && i < layerStride);

				// Check if the node is on a slope steeper than permitted
				if (walkable && useRaycastNormal) {
					if (didHit) {
						// Take the dot product to find out the cosine of the angle it has (faster than Vector3.Angle)
						float angle = math.dot(nodeNormals[i], upNative);

						// Check if the ground is flat enough to stand on
						if (angle < cosMaxSlopeAngle) {
							walkable = false;
						}
					}
				}

				// Check if there is a node above this one (layered grid graph only)
				if (walkable && i + layerStride < nodeNormals.Length && math.any(nodeNormals[i + layerStride])) {
					walkable = math.dot(upNative3, nodePositions[i + layerStride] - nodePositions[i]) >= characterHeight;
				}

				nodeWalkable[i] = walkable;
			}
		}
	}

	public interface GridAdjacencyMapper {
		int LayerCount(IntBounds bounds);
		int GetNeighbourIndex(int nodeIndexXZ, int nodeIndex, int direction, NativeArray<int> nodeConnections, NativeArray<int> neighbourOffsets, int layerStride);
		bool HasConnection(int nodeIndex, int direction, NativeArray<int> nodeConnections);
	}

	public struct FlatGridAdjacencyMapper : GridAdjacencyMapper {
		public int LayerCount (IntBounds bounds) {
			UnityEngine.Assertions.Assert.IsTrue(bounds.size.y == 1);
			return 1;
		}
		public int GetNeighbourIndex (int nodeIndexXZ, int nodeIndex, int direction, NativeArray<int> nodeConnections, NativeArray<int> neighbourOffsets, int layerStride) {
			return nodeIndex + neighbourOffsets[direction];
		}
		public bool HasConnection (int nodeIndex, int direction, NativeArray<int> nodeConnections) {
			return ((nodeConnections[nodeIndex] >> direction) & 0x1) != 0;
		}
	}

	public struct LayeredGridAdjacencyMapper : GridAdjacencyMapper {
		public int LayerCount(IntBounds bounds) => bounds.size.y;
		public int GetNeighbourIndex (int nodeIndexXZ, int nodeIndex, int direction, NativeArray<int> nodeConnections, NativeArray<int> neighbourOffsets, int layerStride) {
			return nodeIndexXZ + neighbourOffsets[direction] + ((nodeConnections[nodeIndex] >> LevelGridNode.ConnectionStride*direction) & LevelGridNode.ConnectionMask) * layerStride;
		}
		public bool HasConnection (int nodeIndex, int direction, NativeArray<int> nodeConnections) {
			return ((nodeConnections[nodeIndex] >> LevelGridNode.ConnectionStride*direction) & LevelGridNode.ConnectionMask) != LevelGridNode.NoConnection;
		}
	}

	/// <summary>
	/// Calculates erosion.
	/// Note that to ensure that connections are completely up to date after updating a node you
	/// have to calculate the connections for both the changed node and its neighbours.
	///
	/// In a layered grid graph, this will recalculate the connections for all nodes
	/// in the (x,z) cell (it may have multiple layers of nodes).
	///
	/// See: CalculateConnections(GridNodeBase)
	/// </summary>
	[BurstCompile]
	public struct JobErosion<AdjacencyMapper> : IJob where AdjacencyMapper : GridAdjacencyMapper, new() {
		public IntBounds bounds;
		public IntBounds writeMask;
		public NumNeighbours neighbours;
		public int erosion;
		public bool erosionUsesTags;
		public int erosionStartTag;

		[ReadOnly]
		public NativeArray<int> nodeConnections;

		[ReadOnly]
		public NativeArray<bool> nodeWalkable;

		[WriteOnly]
		public NativeArray<bool> outNodeWalkable;

		public NativeArray<int> nodeTags;

		// Note: the first 3 connections are to nodes with a higher x or z coordinate
		// The last 3 connections are to nodes with a lower x or z coordinate
		// This is required for the grassfire transform to work properly
		// This is a permutation of GridGraph.hexagonNeighbourIndices
		static readonly int[] hexagonNeighbourIndices = { 1, 2, 5, 0, 3, 7 };

		public void Execute () {
			var size = bounds.size;

			Debug.Assert(size.x * size.y * size.z == outNodeWalkable.Length);

			NativeArray<int> neighbourOffsets = new NativeArray<int>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			for (int i = 0; i < 8; i++) neighbourOffsets[i] = GridGraph.neighbourZOffsets[i] * size.x + GridGraph.neighbourXOffsets[i];

			var erosionDistances = new NativeArray<int>(outNodeWalkable.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
			var adjacencyMapper = new AdjacencyMapper();
			var layers = adjacencyMapper.LayerCount(bounds);
			int layerStride = size.x * size.z;
			if (neighbours == NumNeighbours.Six) {
				// Use the grassfire transform: https://en.wikipedia.org/wiki/Grassfire_transform extended to hexagonal graphs
				for (int z = 1; z < size.z - 1; z++) {
					for (int x = 1; x < size.x - 1; x++) {
						int indexXZ = z * size.x + x;
						for (int y = 0; y < layers; y++) {
							int index = indexXZ + y * layerStride;
							int v = int.MaxValue;
							for (int i = 3; i < 6; i++) {
								int connection = hexagonNeighbourIndices[i];
								//if ((nodeConnections[indexXZ] & (1 << connection)) == 0) v = -1;
								//else v = math.min(v, erosionDistances[indexXZ + neighbourOffsets[connection]]);
								if (!adjacencyMapper.HasConnection(index, connection, nodeConnections)) v = -1;
								else v = math.min(v, erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, connection, nodeConnections, neighbourOffsets, layerStride)]);
							}

							erosionDistances[index] = v + 1;
						}
					}
				}

				for (int z = size.z - 2; z > 0; z--) {
					for (int x = size.x - 2; x > 0; x--) {
						int indexXZ = z * size.x + x;
						for (int y = 0; y < layers; y++) {
							int index = indexXZ + y * layerStride;
							int v = int.MaxValue;
							for (int i = 3; i < 6; i++) {
								int connection = hexagonNeighbourIndices[i];
								//if ((nodeConnections[indexXZ] & (1 << connection)) == 0) v = -1;
								//else v = math.min(v, erosionDistances[indexXZ + neighbourOffsets[connection]]);
								if (!adjacencyMapper.HasConnection(index, connection, nodeConnections)) v = -1;
								else v = math.min(v, erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, connection, nodeConnections, neighbourOffsets, layerStride)]);
							}

							erosionDistances[index] = math.min(erosionDistances[index], v + 1);
						}
					}
				}
			} else {
				/* Index offset to get neighbour nodes. Added to a node's index to get a neighbour node index.
				 *
				 * \code
				 *         Z
				 *         |
				 *         |
				 *
				 *      6  2  5
				 *       \ | /
				 * --  3 - X - 1  ----- X
				 *       / | \
				 *      7  0  4
				 *
				 *         |
				 *         |
				 * \endcode
				 */
				const int DirectionDown = 0;
				const int DirectionRight = 1;
				const int DirectionUp = 2;
				const int DirectionLeft = 3;

				// Use the grassfire transform: https://en.wikipedia.org/wiki/Grassfire_transform
				for (int z = 1; z < size.z - 1; z++) {
					for (int x = 1; x < size.x - 1; x++) {
						int indexXZ = z * size.x + x;
						for (int y = 0; y < layers; y++) {
							int index = indexXZ + y * layerStride;
							var v1 = -1;
							if (adjacencyMapper.HasConnection(index, DirectionDown, nodeConnections)) v1 = erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, DirectionDown, nodeConnections, neighbourOffsets, layerStride)];
							var v2 = -1;
							if (adjacencyMapper.HasConnection(index, DirectionLeft, nodeConnections)) v2 = erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, DirectionLeft, nodeConnections, neighbourOffsets, layerStride)];
							erosionDistances[index] = math.min(v1, v2) + 1;
						}
					}
				}

				for (int z = size.z - 2; z > 0; z--) {
					for (int x = size.x - 2; x > 0; x--) {
						int indexXZ = z * size.x + x;
						for (int y = 0; y < layers; y++) {
							int index = indexXZ + y * layerStride;
							var v1 = -1;
							if (adjacencyMapper.HasConnection(index, DirectionUp, nodeConnections)) v1 = erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, DirectionUp, nodeConnections, neighbourOffsets, layerStride)];
							var v2 = -1;
							if (adjacencyMapper.HasConnection(index, DirectionRight, nodeConnections)) v2 = erosionDistances[adjacencyMapper.GetNeighbourIndex(indexXZ, index, DirectionRight, nodeConnections, neighbourOffsets, layerStride)];
							erosionDistances[index] = math.min(erosionDistances[index], math.min(v1, v2) + 1);
						}
					}
				}
			}

			var relativeWriteMask = writeMask.Offset(-bounds.min);
			for (int y = relativeWriteMask.min.y; y < relativeWriteMask.max.y; y++) {
				for (int z = relativeWriteMask.min.z; z < relativeWriteMask.max.z; z++) {
					int rowOffset = y * layerStride + z * size.x;
					for (int x = relativeWriteMask.min.x; x < relativeWriteMask.max.x; x++) {
						int index = rowOffset + x;
						if (erosionUsesTags) {
							outNodeWalkable[index] = nodeWalkable[index];
							if (erosionDistances[index] < erosion) {
								nodeTags[index] = nodeWalkable[index] ? math.min(GraphNode.MaxTagIndex, erosionDistances[index] + erosionStartTag) : 0;
							} else if (nodeTags[index] >= erosionStartTag && nodeTags[index] < erosionStartTag + erosion) {
								// If the node already had a tag that was reserved for erosion, but it shouldn't have that tag, then we remove it.
								nodeTags[index] = 0;
							}
						} else {
							outNodeWalkable[index] = nodeWalkable[index] & (erosionDistances[index] >= erosion);
						}
					}
				}
			}
		}
	}

	/// <summary>
	/// Calculates the grid connections for all nodes.
	///
	/// This is a IJobParallelForBatch job. Calculating the connections in multiple threads is faster
	/// but due to hyperthreading (used on most intel processors) the individual threads will become slower.
	/// It is still worth it though.
	/// </summary>
	[BurstCompile(FloatMode = FloatMode.Fast)]
	public struct JobCalculateConnections : IJobParallelForBatched {
		public float maxStepHeight;
		/// <summary>Normalized up direction</summary>
		public Vector3 up;
		public IntBounds bounds;
		public NumNeighbours neighbours;
		public bool use2D;
		public bool cutCorners;
		public bool maxStepUsesSlope;
		public float characterHeight;
		public bool layeredDataLayout;

		[ReadOnly]
		public NativeArray<bool> nodeWalkable;

		[ReadOnly]
		public NativeArray<float4> nodeNormals;

		[ReadOnly]
		public NativeArray<Vector3> nodePositions;

		/// <summary>All bitpacked node connections</summary>
		[WriteOnly]
		public NativeArray<int> nodeConnections;

		public bool allowBoundsChecks => false;

		/// <summary>
		/// Check if a connection to node B is valid.
		/// Node A is assumed to be walkable already
		/// </summary>
		bool IsValidConnection (float4 nodePosA, float4 nodeNormalA, int nodeB, float4 up) {
			if (!nodeWalkable[nodeB]) return false;

			float4 nodePosB = new float4(nodePositions[nodeB].x, nodePositions[nodeB].y, nodePositions[nodeB].z, 0);
			if (!maxStepUsesSlope) {
				// Check their differences along the Y coordinate (well, the up direction really. It is not necessarily the Y axis).
				return math.abs(math.dot(up, nodePosB - nodePosA)) <= maxStepHeight;
			} else {
				float4 v = nodePosB - nodePosA;
				float heightDifference = math.dot(v, up);

				// Check if the step is small enough.
				// This is a fast path for the common case.
				if (math.abs(heightDifference) <= maxStepHeight) return true;

				float4 v_flat = (v - heightDifference * up) * 0.5f;

				// Math!
				// Calculates the approximate offset along the up direction
				// that the ground will have moved at the midpoint between the
				// nodes compared to the nodes' center points.
				float NDotU = math.dot(nodeNormalA, up);
				float offsetA = -math.dot(nodeNormalA - NDotU * up, v_flat);

				float4 nodeNormalB = nodeNormals[nodeB];
				NDotU = math.dot(nodeNormalB, up);
				float offsetB = math.dot(nodeNormalB - NDotU * up, v_flat);

				// Check the height difference with slopes taken into account.
				// Note that since we also do the heightDifference check above we will ensure slope offsets do not increase the height difference.
				// If we allowed this then some connections might not be valid near the start of steep slopes.
				return math.abs(heightDifference + offsetB - offsetA) <= maxStepHeight;
			}
		}

		public void Execute (int start, int count) {
			if (layeredDataLayout) ExecuteLayered(start, count);
			else ExecuteFlat(start, count);
		}

		public void ExecuteFlat (int start, int count) {
			if (maxStepHeight <= 0 || use2D) maxStepHeight = float.PositiveInfinity;

			float4 up = new float4(this.up.x, this.up.y, this.up.z, 0);

			int3 size = bounds.size;
			NativeArray<int> neighbourOffsets = new NativeArray<int>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			for (int i = 0; i < 8; i++) neighbourOffsets[i] = GridGraph.neighbourZOffsets[i] * size.x + GridGraph.neighbourXOffsets[i];

			int hexagonConnectionMask = 0;
			for (int i = 0; i < GridGraph.hexagonNeighbourIndices.Length; i++) hexagonConnectionMask |= 1 << GridGraph.hexagonNeighbourIndices[i];

			// The loop is parallelized over z coordinates
			for (int z = start; z < start + count; z++) {
				for (int x = 0; x < size.x; x++) {
					// Bitpacked connections
					// bit 0 is set if connection 0 is enabled
					// bit 1 is set if connection 1 is enabled etc.
					int conns = 0;
					int nodeIndex = z * size.x + x;
					float4 pos = new float4(nodePositions[nodeIndex].x, nodePositions[nodeIndex].y, nodePositions[nodeIndex].z, 0);
					float4 normal = nodeNormals[nodeIndex];

					if (nodeWalkable[nodeIndex]) {
						if (x != 0 && z != 0 && x != size.x - 1 && z != size.z - 1) {
							// Inner part of the grid. We can skip bounds checking for these.
							for (int i = 0; i < 8; i++) {
								int neighbourIndex = nodeIndex + neighbourOffsets[i];
								if (IsValidConnection(pos, normal, neighbourIndex, up)) {
									// Enable connection i
									conns |= 1 << i;
								}
							}
						} else {
							// Border node. These require bounds checking
							for (int i = 0; i < 8; i++) {
								int nx = x + GridGraph.neighbourXOffsets[i];
								int nz = z + GridGraph.neighbourZOffsets[i];

								// Check if the new position is inside the grid
								if (nx >= 0 && nz >= 0 && nx < size.x && nz < size.z) {
									int neighbourIndex = nodeIndex + neighbourOffsets[i];
									if (IsValidConnection(pos, normal, neighbourIndex, up)) {
										// Enable connection i
										conns |= 1 << i;
									}
								}
							}
						}
					}

					nodeConnections[nodeIndex] = GridNode.FilterDiagonalConnections(conns, neighbours, cutCorners);
				}
			}
		}

		public void ExecuteLayered (int start, int count) {
			if (maxStepHeight <= 0 || use2D) maxStepHeight = float.PositiveInfinity;

			float4 up = new float4(this.up.x, this.up.y, this.up.z, 0);

			int3 size = bounds.size;
			NativeArray<int> neighbourOffsets = new NativeArray<int>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			for (int i = 0; i < 8; i++) neighbourOffsets[i] = GridGraph.neighbourZOffsets[i] * size.x + GridGraph.neighbourXOffsets[i];

			var layerStride = size.z*size.x;
			for (int y = 0; y < size.y; y++) {
				// The loop is parallelized over z coordinates
				for (int z = start; z < start + count; z++) {
					for (int x = 0; x < size.x; x++) {
						// Bitpacked connections
						// bit 0 is set if connection 0 is enabled
						// bit 1 is set if connection 1 is enabled etc.
						int conns = 0;
						int nodeIndexXZ = z * size.x + x;
						int nodeIndex = nodeIndexXZ + y * layerStride;
						float4 pos = new float4(nodePositions[nodeIndex], 0);
						float4 normal = nodeNormals[nodeIndex];

						if (nodeWalkable[nodeIndex]) {
							var ourY = math.dot(up, pos);

							float ourHeight;
							if (y == size.y-1 || !math.any(nodeNormals[nodeIndex + layerStride])) {
								ourHeight = float.PositiveInfinity;
							} else {
								var nodeAboveNeighbourPos = new float4(nodePositions[nodeIndex + layerStride], 0);
								ourHeight = math.max(0, math.dot(up, nodeAboveNeighbourPos) - ourY);
							}

							for (int i = 0; i < 8; i++) {
								int nx = x + GridGraph.neighbourXOffsets[i];
								int nz = z + GridGraph.neighbourZOffsets[i];

								// Check if the new position is inside the grid
								int conn = LevelGridNode.NoConnection;
								if (nx >= 0 && nz >= 0 && nx < size.x && nz < size.z) {
									int neighbourStartIndex = nodeIndexXZ + neighbourOffsets[i];
									for (int y2 = 0; y2 < size.y; y2++) {
										var neighbourIndex = neighbourStartIndex + y2 * layerStride;
										float4 nodePosB = new float4(nodePositions[neighbourIndex], 0);
										var neighbourY = math.dot(up, nodePosB);
										// Is there a node above this one
										float neighbourHeight;
										if (y2 == size.y-1 || !math.any(nodeNormals[neighbourIndex + layerStride])) {
											neighbourHeight = float.PositiveInfinity;
										} else {
											var nodeAboveNeighbourPos = new float4(nodePositions[neighbourIndex + layerStride], 0);
											neighbourHeight = math.max(0, math.dot(up, nodeAboveNeighbourPos) - neighbourY);
										}

										float bottom = math.max(neighbourY, ourY);
										float top = math.min(neighbourY + neighbourHeight, ourY + ourHeight);

										float dist = top-bottom;

										if (dist >= characterHeight && IsValidConnection(pos, normal, neighbourIndex, up)) {
											conn = y2;
										}
									}
								}

								conns |= conn << LevelGridNode.ConnectionStride*i;
							}
						} else {
							conns = -1;
						}

						nodeConnections[nodeIndex] = conns;
					}
				}
			}
		}
	}

	[BurstCompile]
	public struct JobFilterDiagonalConnections : IJobParallelForBatched {
		public IntBounds bounds;
		public NumNeighbours neighbours;
		public bool cutCorners;

		/// <summary>All bitpacked node connections</summary>
		public NativeArray<int> nodeConnections;

		public bool allowBoundsChecks => false;

		public void Execute (int start, int count) {
			// For single layer graphs this will have already been done in the JobCalculateConnections job
			// but for layered grid graphs we need to handle things differently because the data layout is different

			int3 size = bounds.size;
			NativeArray<int> neighbourOffsets = new NativeArray<int>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

			for (int i = 0; i < 8; i++) neighbourOffsets[i] = GridGraph.neighbourZOffsets[i] * size.x + GridGraph.neighbourXOffsets[i];

			int hexagonConnectionMask = 0;
			for (int i = 0; i < GridGraph.hexagonNeighbourIndices.Length; i++) hexagonConnectionMask |= LevelGridNode.ConnectionMask << (LevelGridNode.ConnectionStride*GridGraph.hexagonNeighbourIndices[i]);

			int adjacencyThreshold = cutCorners ? 1 : 2;
			int layerStride = size.x * size.z;
			for (int y = 0; y < size.y; y++) {
				// The loop is parallelized over z coordinates
				for (int z = start; z < start + count; z++) {
					for (int x = 0; x < size.x; x++) {
						int nodeIndexXZ = z * size.x + x;
						int nodeIndex = nodeIndexXZ + y * layerStride;
						switch (neighbours) {
						case NumNeighbours.Four:
							// Mask out all the diagonal connections
							nodeConnections[nodeIndex] = nodeConnections[nodeIndex] | LevelGridNode.AxisAlignedConnectionsOR;
							break;
						case NumNeighbours.Eight:
							var conns = nodeConnections[nodeIndex];
							// When cutCorners is enabled then the diagonal connection is allowed
							// if at least one axis aligned connection is adjacent to this diagonal.
							// Otherwise both axis aligned connections must be present.
							//
							//   X ----- axis2
							//   | \
							//   |   \
							//   |     \
							// axis1   diagonal
							//
							//         Z
							//         |
							//         |
							//
							//      6  2  5
							//       \ | /
							// --  3 - X - 1  ----- X
							//       / | \
							//      7  0  4
							//
							//         |
							//         |
							//
							for (int dir = 0; dir < 4; dir++) {
								int adjacent = 0;
								var axis1 = (conns >> dir*LevelGridNode.ConnectionStride) & LevelGridNode.ConnectionMask;
								var axis2 = (conns >> ((dir+1) % 4)*LevelGridNode.ConnectionStride) & LevelGridNode.ConnectionMask;
								var diagonal = (conns >> (dir+4)*LevelGridNode.ConnectionStride) & LevelGridNode.ConnectionMask;
								if (diagonal == LevelGridNode.NoConnection) continue;

								if (axis1 != LevelGridNode.NoConnection) {
									// We also check that the neighbour node is also connected to the diagonal node
									var neighbourDir = (dir + 1) % 4;
									var neighbourIndex = nodeIndexXZ + neighbourOffsets[dir] + axis1 * layerStride;
									if (((nodeConnections[neighbourIndex] >> neighbourDir*LevelGridNode.ConnectionStride) & LevelGridNode.ConnectionMask) == diagonal) {
										adjacent++;
									}
								}
								if (axis2 != LevelGridNode.NoConnection) {
									var neighbourDir = dir;
									var neighbourIndex = nodeIndexXZ + neighbourOffsets[(dir+1)%4] + axis2 * layerStride;
									if (((nodeConnections[neighbourIndex] >> neighbourDir*LevelGridNode.ConnectionStride) & LevelGridNode.ConnectionMask) == diagonal) {
										adjacent++;
									}
								}

								if (adjacent < adjacencyThreshold) conns |= LevelGridNode.NoConnection << (dir + 4)*LevelGridNode.ConnectionStride;
							}
							nodeConnections[nodeIndex] = conns;
							break;
						case NumNeighbours.Six:
							// Hexagon layout
							// Note that for layered nodes NoConnection is all bits set (see LevelGridNode.NoConnection)
							// So in contrast to the non-layered grid graph we do a bitwise OR here
							nodeConnections[nodeIndex] = nodeConnections[nodeIndex] | ~hexagonConnectionMask;
							break;
						}
					}
				}
			}
		}
	}

	struct JobCheckCollisions : IJobTimeSliced {
		[ReadOnly]
		public NativeArray<Vector3> nodePositions;
		public NativeArray<bool> collisionResult;
		public GraphCollision collision;
		int startIndex;

		public void Execute () {
			Execute(TimeSlice.Infinite);
		}

		public bool Execute (TimeSlice timeSlice) {
			for (int i = startIndex; i < nodePositions.Length; i++) {
				collisionResult[i] = collisionResult[i] && collision.Check(nodePositions[i]);
				if ((i & 127) == 0 && timeSlice.expired) {
					startIndex = i + 1;
					return false;
				}
			}
			return true;
		}
	}

	public struct JobReadNodeData : IJobParallelForBatched {
		public System.Runtime.InteropServices.GCHandle nodesHandle;
		public uint graphIndex;

		/// <summary>(width, depth) of the array that the <see cref="nodesHandle"/> refers to</summary>
		public int3 nodeArrayBounds;
		public IntBounds dataBounds;

		[WriteOnly]
		public NativeArray<Vector3> nodePositions;

		[WriteOnly]
		public NativeArray<uint> nodePenalties;

		[WriteOnly]
		public NativeArray<int> nodeTags;

		[WriteOnly]
		public NativeArray<int> nodeConnections;

		[WriteOnly]
		public NativeArray<bool> nodeWalkableWithErosion;

		[WriteOnly]
		public NativeArray<bool> nodeWalkable;

		public bool allowBoundsChecks => false;

		public void Execute (int startIndex, int count) {
			// This is a managed type, we need to trick Unity to allow this inside of a job
			var nodes = (GridNodeBase[])nodesHandle.Target;

			var size = dataBounds.size;

			// The data bounds may have more layers than the existing nodes if a new layer is being added.
			// We can only copy from the nodes that exist.
			var layers = math.min(size.y, nodeArrayBounds.y);

			for (int y = 0; y < layers; y++) {
				for (int z = 0; z < size.z; z++) {
					int offset1 = y*size.x*size.z + z*size.x;
					int offset2 = (y + dataBounds.min.y)*nodeArrayBounds.x*nodeArrayBounds.z + (z + dataBounds.min.z)*nodeArrayBounds.x + dataBounds.min.x;
					for (int x = 0; x < size.x; x++) {
						var nodeIdx = offset2 + x;
						var node = nodes[nodeIdx];
						var dataIdx = offset1 + x;
						if (node != null) {
							nodePositions[dataIdx] = (Vector3)node.position;
							nodePenalties[dataIdx] = node.Penalty;
							nodeTags[dataIdx] = (int)node.Tag;
							nodeConnections[dataIdx] = node is GridNode gn? gn.GetAllConnectionInternal() : (int)(node as LevelGridNode).gridConnections;
							nodeWalkableWithErosion[dataIdx] = node.Walkable;
							nodeWalkable[dataIdx] = node.WalkableErosion;
						} else {
							nodePositions[dataIdx] = Vector3.zero;
							nodePenalties[dataIdx] = 0;
							nodeTags[dataIdx] = 0;
							nodeConnections[dataIdx] = 0;
							nodeWalkableWithErosion[dataIdx] = false;
							nodeWalkable[dataIdx] = false;
						}
					}
				}
			}

			// Fill remaining layers with empty node data.
			for (int y = layers; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					int offset1 = y*size.x*size.z + z*size.x;
					for (int x = 0; x < size.x; x++) {
						var dataIdx = offset1 + x;
						nodePositions[dataIdx] = Vector3.zero;
						nodePenalties[dataIdx] = 0;
						nodeTags[dataIdx] = 0;
						nodeConnections[dataIdx] = 0;
						nodeWalkableWithErosion[dataIdx] = false;
						nodeWalkable[dataIdx] = false;
					}
				}
			}
		}
	}

	public struct JobAllocateNodes : IJob {
		public AstarPath active;
		[ReadOnly]
		public NativeArray<float4> nodeNormals;
		public IntBounds dataBounds;
		public int3 nodeArrayBounds;
		public GridNodeBase[] nodes;
		public System.Func<GridNodeBase> newGridNodeDelegate;

		public void Execute () {
			var size = dataBounds.size;

			// Start at y=1 because all nodes at y=0 are guaranteed to already be allocated (they are always allocated in a layered grid graph).
			for (int y = 1; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					var offset1 = (y * size.z + z) * size.x;
					var offset2 = ((y + dataBounds.min.y) * nodeArrayBounds.z + (z + dataBounds.min.z)) * nodeArrayBounds.x + dataBounds.min.x;
					for (int x = 0; x < size.x; x++) {
						var shouldHaveNode = math.any(nodeNormals[offset1 + x]);
						var nodeIndex = offset2 + x;
						var hasNode = nodes[nodeIndex] != null;
						if (shouldHaveNode && !hasNode) {
							var node = nodes[nodeIndex] = newGridNodeDelegate();
							active.InitializeNode(node);
						} else if (!shouldHaveNode && hasNode) {
							// Clear custom connections first and clear connections from other nodes to this one
							nodes[nodeIndex].ClearCustomConnections(true);
							// Clear grid connections without clearing the connections from other nodes to this one (a bit slow)
							// Since this is inside a graph update we guarantee that the grid connections will be correct at the end
							// of the update anyway
							nodes[nodeIndex].ResetConnectionsInternal();
							nodes[nodeIndex].Destroy();
							nodes[nodeIndex] = null;
						}
					}
				}
			}
		}
	}

	public struct JobDirtyNodes : IJob {
		public System.Runtime.InteropServices.GCHandle nodesHandle;
		/// <summary>(width, depth) of the array that the <see cref="nodesHandle"/> refers to</summary>
		public int3 nodeArrayBounds;
		public IntBounds dataBounds;

		public void Execute () {
			// This is a managed type, we need to trick Unity to allow this inside of a job
			var nodes = (GridNodeBase[])nodesHandle.Target;

			for (int y = dataBounds.min.y; y < dataBounds.max.y; y++) {
				for (int z = dataBounds.min.z; z < dataBounds.max.z; z++) {
					var rowOffset = y*nodeArrayBounds.x*nodeArrayBounds.z + z*nodeArrayBounds.x;
					for (int x = dataBounds.min.x; x < dataBounds.max.x; x++) {
						var node = nodes[rowOffset + x];
						if (node != null) AstarPath.active.hierarchicalGraph.AddDirtyNode(node);
					}
				}
			}
		}
	}

	public struct JobAssignNodeData : IJobParallelForBatched {
		public System.Runtime.InteropServices.GCHandle nodesHandle;
		public uint graphIndex;

		/// <summary>(width, depth) of the array that the <see cref="nodesHandle"/> refers to</summary>
		public int3 nodeArrayBounds;
		public IntBounds dataBounds;
		public IntBounds writeMask;

		[ReadOnly]
		public NativeArray<Vector3> nodePositions;

		[ReadOnly]
		public NativeArray<uint> nodePenalties;

		[ReadOnly]
		public NativeArray<int> nodeTags;

		[ReadOnly]
		public NativeArray<int> nodeConnections;

		[ReadOnly]
		public NativeArray<bool> nodeWalkableWithErosion;

		[ReadOnly]
		public NativeArray<bool> nodeWalkable;

		public bool allowBoundsChecks => false;

		public void Execute (int startIndex, int count) {
			// This is a managed type, we need to trick Unity to allow this inside of a job
			var nodes = (GridNodeBase[])nodesHandle.Target;

			var relativeMask = writeMask.Offset(-dataBounds.min);

			// Determinstically convert the indices to rows. It is much easier to process a number of whole rows.
			var zstart = startIndex / (dataBounds.size.x * dataBounds.size.y);
			var zend = (startIndex+count) / (dataBounds.size.x * dataBounds.size.y);

			relativeMask.min.z = math.max(relativeMask.min.z, zstart);
			relativeMask.max.z = math.min(relativeMask.max.z, zend);

			for (int y = relativeMask.min.y; y < relativeMask.max.y; y++) {
				for (int z = relativeMask.min.z; z < relativeMask.max.z; z++) {
					var rowOffset1 = (y*dataBounds.size.z + z)*dataBounds.size.x;
					var rowOffset2 = (z + dataBounds.min.z)*nodeArrayBounds.x + dataBounds.min.x;
					var rowOffset3 = ((y + dataBounds.min.y)*nodeArrayBounds.z + (z + dataBounds.min.z))*nodeArrayBounds.x + dataBounds.min.x;
					for (int x = relativeMask.min.x; x < relativeMask.max.x; x++) {
						int dataIndex = rowOffset1 + x;
						int nodeIndex = rowOffset3 + x;
						var node = nodes[nodeIndex];
						if (node != null) {
							node.GraphIndex = graphIndex;
							node.NodeInGridIndex = rowOffset2 + x;
							node.position = (Int3)nodePositions[dataIndex];
							node.Penalty = nodePenalties[dataIndex];
							node.Tag = (uint)nodeTags[dataIndex];
							if (node is GridNode gridNode) {
								gridNode.SetAllConnectionInternal(nodeConnections[dataIndex]);
							} else if (node is LevelGridNode levelGridNode) {
								levelGridNode.LayerCoordinateInGrid = y + dataBounds.min.y;
								levelGridNode.SetAllConnectionInternal(nodeConnections[dataIndex]);
							}
							node.Walkable = nodeWalkableWithErosion[dataIndex];
							node.WalkableErosion = nodeWalkable[dataIndex];
						}
					}
				}
			}
		}
	}
}
