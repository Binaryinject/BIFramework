namespace Pathfinding.RVO {
	using Pathfinding;
	using UnityEngine;
	using Pathfinding.Util;
	using Unity.Mathematics;
	using Unity.Collections;
	using Unity.Jobs;
	using System.Collections.Generic;

	public struct RVOObstacleCache {
		Dictionary<ulong, int> cache;
		// HashSet<ulong> used;
		ManagedObstacle[] managedObstacles;
		Stack<int> freeObstacleIndices;
		Unity.Collections.NativeList<ObstacleSegment> obstaclesScratch;

		public struct ObstacleSegment {
			public float3 vertex1;
			public float3 vertex2;
			public int vertex1LinkId;
			public int vertex2LinkId;
		}

		public RVOObstacleCache(int initialCapacity) {
			cache = new Dictionary<ulong, int>();
			freeObstacleIndices = new Stack<int>();
			managedObstacles = new ManagedObstacle[initialCapacity];
			for (int i = managedObstacles.Length - 1; i >= 0; i--) freeObstacleIndices.Push(i);
			obstaclesScratch = new Unity.Collections.NativeList<ObstacleSegment>(16, Allocator.Persistent);
		}

		ulong HashKey (GraphNode sourceNode, int traversableTags, SimpleMovementPlane movementPlane) {
			var hash = (ulong)sourceNode.NodeIndex;
			hash = hash * 786433 ^ (ulong)traversableTags;
			// The rotation is not particularly important for the obstacle. It's only used
			// to simplify the output a bit. So we allow similar rotations to share the same hash.
			const float RotationQuantization = 4;
			hash = hash * 786433 ^ (ulong)Mathf.RoundToInt(movementPlane.rotation.x*RotationQuantization);
			hash = hash * 786433 ^ (ulong)Mathf.RoundToInt(movementPlane.rotation.y*RotationQuantization);
			hash = hash * 786433 ^ (ulong)Mathf.RoundToInt(movementPlane.rotation.z*RotationQuantization);
			hash = hash * 786433 ^ (ulong)Mathf.RoundToInt(movementPlane.rotation.w*RotationQuantization);
			return hash;
		}

		public void FreeUnusedObstacles (ref SimulatorBurst.ObstacleData obstacleData, NativeArray<int> usedObstacles, int numUsedObstacles) {
			var used = ArrayPool<bool>.Claim(managedObstacles.Length);
			for (int i = 0; i < managedObstacles.Length; i++) used[i] = false;
			for (int i = 0; i < numUsedObstacles; i++) {
				if (usedObstacles[i] != -1) {
					used[usedObstacles[i]] = true;
				}
			}
			for (int i = managedObstacles.Length - 1; i >= 0; i--) {
				if (!used[i] && managedObstacles[i].hash != 0) {
					freeObstacleIndices.Push(i);
					obstacleData.obstacleVertices.Free(obstacleData.obstacles[i].verticesAllocation);
					obstacleData.obstacleVertexGroups.Free(obstacleData.obstacles[i].groupsAllocation);
					cache.Remove(managedObstacles[i].hash);
					ListPool<int>.Release(ref managedObstacles[i].hierarchicalAreas);
					managedObstacles[i] = default(ManagedObstacle);
				}
			}
			ArrayPool<bool>.Release(ref used);
		}

		public void Dispose () {
			obstaclesScratch.Dispose();
		}

		public int GetObstaclesAround (GraphNode sourceNode, int traversableTags, SimpleMovementPlane movementPlane, ref SimulatorBurst.ObstacleData obstacleData) {
			if (sourceNode == null) return -1;

			var hash = HashKey(sourceNode, traversableTags, movementPlane);
			// used.Add(hash);

			if (cache.ContainsKey(hash)) {
				return cache[hash];
			}

			UnityEngine.Profiling.Profiler.BeginSample("Calculate Obstacles");
			UnityEngine.Profiling.Profiler.BeginSample("BFS");
			const int SearchDepth = 5;
			var nearbyNodes = PathUtilities.BFS(sourceNode, SearchDepth, traversableTags);
			UnityEngine.Profiling.Profiler.EndSample();
			UnityEngine.Profiling.Profiler.BeginSample("Find edges");
			var obstacles = obstaclesScratch;
			obstacles.Clear();
			for (int i = 0; i < nearbyNodes.Count; i++) {
				var node = nearbyNodes[i];
				if (node is TriangleMeshNode tnode) {
					var used = 0;
					for (int j = 0; j < tnode.connections.Length; j++) {
						var conn = tnode.connections[j];
						if (conn.shapeEdge != Connection.NoSharedEdge) {
							used |= 1 << conn.shapeEdge;
						}
					}

					for (int edgeIndex = 0; edgeIndex < tnode.GetVertexCount(); edgeIndex++) {
						if ((used & (1 << edgeIndex)) == 0) {
							// This edge is not shared, so it's a border edge
							var leftVertex = tnode.GetVertex(edgeIndex);
							var rightVertex = tnode.GetVertex((edgeIndex + 1) % tnode.GetVertexCount());
							var leftVertexHash = leftVertex.GetHashCode();
							var rightVertexHash = rightVertex.GetHashCode();

							obstacles.Add(new ObstacleSegment {
								vertex1 = (Vector3)leftVertex,
								vertex2 = (Vector3)rightVertex,
								vertex1LinkId = leftVertexHash,
								vertex2LinkId = rightVertexHash,
							});
						}
					}
				} else if (node is GridNodeBase gnode) {
					var graph = gnode.Graph as GridGraph;
					for (int dir = 0; dir < 4; dir++) {
						if (!gnode.HasConnectionInDirection(dir)) {
							// ┌─────────┬─────────┐
							// │         │         │
							// │   nl1   │   nl2   │     ^
							// │         │         │     |
							// ├────────vL─────────┤     dl
							// │         │#########│
							// │   node  │#########│     dir->
							// │         │#########│
							// ├────────vR─────────┤     dr
							// │         │         │      |
							// │   nr1   │   nr2   │      v
							// │         │         │
							// └─────────┴─────────┘
							var dl = (dir + 1) % 4;
							var dr = (dir - 1 + 4) % 4;
							// TODO: Filter by tags and ITraversalProvider
							var nl1 = gnode.GetNeighbourAlongDirection(dl);
							var nr1 = gnode.GetNeighbourAlongDirection(dr);

							int leftVertexHash;
							if (nl1 != null) {
								var nl2 = nl1.GetNeighbourAlongDirection(dir);
								if (nl2 != null) {
									// Outer corner. Uniquely determined by the 3 nodes that touch the corner.
									leftVertexHash = (gnode.NodeIndex + nl1.NodeIndex + nl2.NodeIndex) * (gnode.NodeIndex ^ nl1.NodeIndex ^ nl2.NodeIndex);
								} else {
									// Straight wall. Uniquely determined by the 2 nodes that touch the corner and the direction to the wall.
									leftVertexHash = (gnode.NodeIndex + nl1.NodeIndex) * (gnode.NodeIndex ^ nl1.NodeIndex) ^ dir;
								}
							} else {
								// Inner corner. Uniquely determined by the single node that touches the corner and the direction to it.
								var diagonalToCorner = dir + 4;
								leftVertexHash = (gnode.NodeIndex * 31) ^ diagonalToCorner;
							}

							int rightVertexHash;
							if (nr1 != null) {
								var nr2 = nr1.GetNeighbourAlongDirection(dir);
								if (nr2 != null) {
									// Outer corner. Uniquely determined by the 3 nodes that touch the corner.
									rightVertexHash = (gnode.NodeIndex + nr1.NodeIndex + nr2.NodeIndex) * (gnode.NodeIndex ^ nr1.NodeIndex ^ nr2.NodeIndex);
								} else {
									// Straight wall. Uniquely determined by the 2 nodes that touch the corner and the direction to the wall.
									rightVertexHash = (gnode.NodeIndex + nr1.NodeIndex) * (gnode.NodeIndex ^ nr1.NodeIndex) ^ dir;
								}
							} else {
								// Inner corner. Uniquely determined by the single node that touches the corner and the direction to it.
								// Note: It's not a typo that we use `dr+4` here and `dir+4` above. They are different directions.
								var diagonalToCorner = dr + 4;
								rightVertexHash = (gnode.NodeIndex * 31) ^ diagonalToCorner;
							}

							var localPos = graph.transform.InverseTransform((Vector3)gnode.position);
							var y0 = localPos.y;

							var leftLocalV = localPos + 0.5f * new Vector3(GridGraph.neighbourXOffsets[dir] + GridGraph.neighbourXOffsets[dl], 0, GridGraph.neighbourZOffsets[dir] + GridGraph.neighbourZOffsets[dl]);
							var rightLocalV = localPos + 0.5f * new Vector3(GridGraph.neighbourXOffsets[dir] + GridGraph.neighbourXOffsets[dr], 0, GridGraph.neighbourZOffsets[dir] + GridGraph.neighbourZOffsets[dr]);
							var leftV = graph.transform.Transform(leftLocalV);
							var rightV = graph.transform.Transform(rightLocalV);

							obstacles.Add(new ObstacleSegment {
								vertex1 = graph.transform.Transform(leftLocalV),
								vertex2 = graph.transform.Transform(rightLocalV),
								vertex1LinkId = leftVertexHash,
								vertex2LinkId = rightVertexHash,
							});
						}
					}
				}
			}
			ListPool<GraphNode>.Release(ref nearbyNodes);

			UnityEngine.Profiling.Profiler.EndSample();

			if (obstacles.Length == 0) {
				UnityEngine.Profiling.Profiler.EndSample();
				return -1;
			}

			UnityEngine.Profiling.Profiler.BeginSample("Collect contours");

			if (freeObstacleIndices.Count == 0) {
				var prevLen = managedObstacles.Length;
				var newLen = math.max(4, managedObstacles.Length*2);
				Memory.Realloc(ref managedObstacles, newLen);
				Memory.Realloc(ref obstacleData.obstacles, newLen, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				for (int i = newLen - 1; i >= prevLen; i--) freeObstacleIndices.Push(i);
			}

			var obstacleId = freeObstacleIndices.Pop();

			new TraceContoursJob {
				obstacles = obstacles,
				movementPlane = movementPlane,
				obstacleId = obstacleId,
				outputObstacles = obstacleData.obstacles,
				verticesAllocator = obstacleData.obstacleVertices,
				obstaclesAllocator = obstacleData.obstacleVertexGroups,
			}.Run();

			UnityEngine.Profiling.Profiler.EndSample();
			managedObstacles[obstacleId] = new ManagedObstacle {
				hash = hash,
				hierarchicalAreas = ListPool<int>.Claim(),
			};

			cache.Add(hash, obstacleId);
			UnityEngine.Profiling.Profiler.EndSample();
			return obstacleId;
		}

		[Unity.Burst.BurstCompile]
		struct TraceContoursJob : IJob {
			// Obstacle segments, typically the borders of the navmesh. In no particular order.
			// Each edge must be oriented the same way (e.g. all clockwise, or all counter-clockwise around the obstacles).
			[ReadOnly]
			public NativeList<ObstacleSegment> obstacles;
			[ReadOnly]
			public SimpleMovementPlane movementPlane;
			public int obstacleId;
			public NativeArray<UnmanagedObstacle> outputObstacles;
			public SlabAllocator<float3> verticesAllocator;
			public SlabAllocator<ObstacleVertexGroup> obstaclesAllocator;

			public void Execute () {
				var hasInEdge = new Unity.Collections.NativeParallelHashSet<int>(obstacles.Length, Unity.Collections.Allocator.Temp);
				var traceLookup = new Unity.Collections.NativeParallelHashMap<int, int>(obstacles.Length, Unity.Collections.Allocator.Temp);
				var visited = new NativeArray<bool>(obstacles.Length, Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.ClearMemory);
				for (int i = 0; i < obstacles.Length; i++) {
					var obstacle = obstacles[i];
					// Add the edge to the lookup. But if it already exists, ignore it.
					// That it already exists is very much a special case that can happen if there is
					// overlapping geometry (typically added by a NavmeshAdd component).
					// In that case the "outer edge" that we want to trace is kinda undefined, but we will
					// do our best with it.
					if (traceLookup.TryAdd(obstacle.vertex1LinkId, i)) {
						hasInEdge.Add(obstacle.vertex2LinkId);
					} else {
						visited[i] = true;
					}
				}

				var outputMetadata = new NativeList<ObstacleVertexGroup>(16, Allocator.Temp);
				var outputVertices = new NativeList<float3>(16, Allocator.Temp);

				// We now have a hashmap of directed edges (vertex1 -> vertex2) such that these edges are directed the same (cw or ccw), and "outer edges".
				// We can now follow these directed edges to trace out the contours of the mesh.
				for (int allowLoops = 0; allowLoops <= 1; allowLoops++) {
					for (int i = 0; i < obstacles.Length; i++) {
						if (!visited[i] && (allowLoops == 1 || !hasInEdge.Contains(obstacles[i].vertex1LinkId))) {
							int startVertices = outputVertices.Length;
							outputVertices.Add(obstacles[i].vertex1);

							var lastAdded = obstacles[i].vertex1;
							var candidateVertex = obstacles[i].vertex2;
							var index = i;
							var obstacleType = ObstacleType.Chain;
							while (true) {
								UnityEngine.Assertions.Assert.IsFalse(visited[index]);
								visited[index] = true;
								float3 nextVertex;
								if (traceLookup.TryGetValue(obstacles[index].vertex2LinkId, out int nextIndex)) {
									nextVertex = 0.5f * (obstacles[index].vertex2 + obstacles[nextIndex].vertex1);
								} else {
									nextVertex = obstacles[index].vertex2;
									nextIndex = -1;
								}

								// Try to simplify and see if we even need to add the vertex C.
								var p1 = movementPlane.ToPlane(lastAdded);
								var p2 = movementPlane.ToPlane(nextVertex);
								var p3 = movementPlane.ToPlane(candidateVertex);
								var d1 = p2 - p1;
								var d2 = p3 - p1;
								var det = (d1.x*d2.y - d1.y*d2.x);
								if (Unity.Mathematics.math.abs(det) < 0.01f) {
									// We don't need to add candidateVertex. It's just a straight line
								} else {
									outputVertices.Add(candidateVertex);
									lastAdded = candidateVertex;
								}

								if (nextIndex == i) {
									// Loop
									outputVertices[startVertices] = nextVertex;
									obstacleType = ObstacleType.Loop;
									break;
								} else if (nextIndex == -1) {
									// End of chain
									outputVertices.Add(nextVertex);
									break;
								}

								index = nextIndex;
								candidateVertex = nextVertex;
							}

							outputMetadata.Add(new ObstacleVertexGroup {
								type = obstacleType,
								vertexCount = outputVertices.Length - startVertices,
							});
						}
					}
				}

				var obstacleSet = obstaclesAllocator.Allocate(outputMetadata);
				var vertexSet = verticesAllocator.Allocate(outputVertices);
				outputObstacles[obstacleId] = new UnmanagedObstacle {
					verticesAllocation = vertexSet,
					groupsAllocation = obstacleSet,
				};
			}
		}
	}
}
