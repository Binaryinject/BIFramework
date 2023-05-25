using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

namespace Pathfinding.Recast {
	using System;
	using Pathfinding;
	using Pathfinding.Voxels.Burst;
	using Pathfinding.Util;
	using Pathfinding.Jobs;

	[BurstCompile]
	internal class RecastMeshGathererBurst {
		readonly int terrainSampleSize;
		readonly LayerMask mask;
		readonly List<string> tagMask;
		readonly float colliderRasterizeDetail;
		readonly Bounds bounds;
		readonly UnityEngine.SceneManagement.Scene scene;
		Dictionary<MeshCacheItem, int> cachedMeshes = new Dictionary<MeshCacheItem, int>();
		Dictionary<GameObject, TreeInfo> cachedTreePrefabs = new Dictionary<GameObject, TreeInfo>();
		List<NativeArray<Vector3> > vertexBuffers;
		List<NativeArray<int> > triangleBuffers;
		List<Mesh> meshData;
#if UNITY_EDITOR
		List<Mesh> meshesUnreadableAtRuntime = new List<Mesh>();
#else
		bool anyNonReadableMesh = false;
#endif

		List<GatheredMesh> meshes;

		public RecastMeshGathererBurst (UnityEngine.SceneManagement.Scene scene, Bounds bounds, int terrainSampleSize, LayerMask mask, List<string> tagMask, float colliderRasterizeDetail) {
			// Clamp to at least 1 since that's the resolution of the heightmap
			terrainSampleSize = Math.Max(terrainSampleSize, 1);

			this.bounds = bounds;
			this.terrainSampleSize = terrainSampleSize;
			this.mask = mask;
			this.tagMask = tagMask ?? new List<string>();
			this.colliderRasterizeDetail = colliderRasterizeDetail;
			this.scene = scene;
			meshes = ListPool<GatheredMesh>.Claim();
			vertexBuffers = ListPool<NativeArray<Vector3> >.Claim();
			triangleBuffers = ListPool<NativeArray<int> >.Claim();
			cachedMeshes = ObjectPoolSimple<Dictionary<MeshCacheItem, int> >.Claim();
			meshData = ListPool<Mesh>.Claim();
		}

		struct TreeInfo {
			public List<GatheredMesh> submeshes;
			public bool supportsRotation;
		}

		public struct MeshCollection : IArenaDisposable {
			List<NativeArray<Vector3> > vertexBuffers;
			List<NativeArray<int> > triangleBuffers;
			public NativeArray<RasterizationMesh> meshes;
#if UNITY_EDITOR
			public List<Mesh> meshesUnreadableAtRuntime;
#endif

			public MeshCollection (List<NativeArray<Vector3> > vertexBuffers, List<NativeArray<int> > triangleBuffers, NativeArray<RasterizationMesh> meshes
#if UNITY_EDITOR
								   , List<Mesh> meshesUnreadableAtRuntime
#endif
								   ) {
				this.vertexBuffers = vertexBuffers;
				this.triangleBuffers = triangleBuffers;
				this.meshes = meshes;
#if UNITY_EDITOR
				this.meshesUnreadableAtRuntime = meshesUnreadableAtRuntime;
#endif
			}

			void IArenaDisposable.DisposeWith (Pathfinding.Jobs.DisposeArena arena) {
				for (int i = 0; i < vertexBuffers.Count; i++) {
					arena.Add(vertexBuffers[i]);
					arena.Add(triangleBuffers[i]);
				}
				arena.Add(meshes);
			}
		}

		[BurstCompile]
		[AOT.MonoPInvokeCallback(typeof(CalculateBoundsDelegate))]
		unsafe static void CalculateBounds (float3* vertices, int numVertices, ref float4x4 localToWorldMatrix, out Bounds bounds) {
			if (numVertices == 0) {
				bounds = new Bounds();
			} else {
				float3 max = float.NegativeInfinity;
				float3 min = float.PositiveInfinity;
				for (int i = 0; i < numVertices; i++) {
					var v = math.mul(localToWorldMatrix, new float4(vertices[i], 1)).xyz;
					max = math.max(max, v);
					min = math.min(min, v);
				}
				bounds = new Bounds((min+max)*0.5f, max-min);
			}
		}

		unsafe delegate void CalculateBoundsDelegate(float3* vertices, int numVertices, ref float4x4 localToWorldMatrix, out Bounds bounds);
		private readonly unsafe static CalculateBoundsDelegate CalculateBoundsInvoke = BurstCompiler.CompileFunctionPointer<CalculateBoundsDelegate>(CalculateBounds).Invoke;

		public MeshCollection Finalize () {
#if UNITY_2020_1_OR_NEWER
#if UNITY_EDITOR
			// This skips the Mesh.isReadable check
			Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(meshData);
#else
			Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(meshData);
#endif
			var meshes = new NativeArray<RasterizationMesh>(this.meshes.Count, Allocator.Persistent);
			int meshBufferOffset = vertexBuffers.Count;

			UnityEngine.Profiling.Profiler.BeginSample("Copying vertices");
			// TODO: We should be able to hold the `data` for the whole scan and not have to copy all vertices/triangles
			for (int i = 0; i < data.Length; i++) {
				Pathfinding.Util.MeshUtility.GetMeshData(data, i, out var verts, out var tris);
				vertexBuffers.Add(verts);
				triangleBuffers.Add(tris);
			}
			UnityEngine.Profiling.Profiler.EndSample();

			UnityEngine.Profiling.Profiler.BeginSample("Creating RasterizationMeshes");
			for (int i = 0; i < meshes.Length; i++) {
				var gatheredMesh = this.meshes[i];
				int bufferIndex;
				if (gatheredMesh.meshDataIndex >= 0) {
					bufferIndex = meshBufferOffset + gatheredMesh.meshDataIndex;
				} else {
					bufferIndex = -(gatheredMesh.meshDataIndex+1);
				}

				var bounds = gatheredMesh.bounds;
				var slice = vertexBuffers[bufferIndex].Reinterpret<float3>();
				if (bounds == new Bounds()) {
					// Recalculate bounding box
					float4x4 m = gatheredMesh.matrix;
					unsafe {
						CalculateBoundsInvoke((float3*)slice.GetUnsafeReadOnlyPtr(), slice.Length, ref m, out bounds);
					}
				}

				var triangles = triangleBuffers[bufferIndex];
				meshes[i] = new RasterizationMesh {
					vertices = new UnsafeSpan<float3>(slice),
					triangles = new UnsafeSpan<int>(triangles.Slice(0, gatheredMesh.indicesCount != -1 ? gatheredMesh.indicesCount : triangles.Length)),
					area = gatheredMesh.area,
					areaIsTag = gatheredMesh.areaIsTag,
					bounds = bounds,
					matrix = gatheredMesh.matrix,
					solid = gatheredMesh.solid,
				};
			}
			UnityEngine.Profiling.Profiler.EndSample();

			cachedMeshes.Clear();
			ObjectPoolSimple<Dictionary<MeshCacheItem, int> >.Release(ref cachedMeshes);
			ListPool<GatheredMesh>.Release(ref this.meshes);

			data.Dispose();

			return new MeshCollection(
				vertexBuffers,
				triangleBuffers,
				meshes
#if UNITY_EDITOR
				, this.meshesUnreadableAtRuntime
#endif
				);
#else
			throw new System.NotImplementedException("The burst version of recast is only supported in Unity 2020.1 or later");
#endif
		}

		int AddMeshBuffers (Vector3[] vertices, int[] triangles) {
			return AddMeshBuffers(new NativeArray<Vector3>(vertices, Allocator.Persistent), new NativeArray<int>(triangles, Allocator.Persistent));
		}

		int AddMeshBuffers (NativeArray<Vector3> vertices, NativeArray<int> triangles) {
			var meshDataIndex = -vertexBuffers.Count-1;

			vertexBuffers.Add(vertices);
			triangleBuffers.Add(triangles);
			return meshDataIndex;
		}

		public struct GatheredMesh {
			public int meshDataIndex;
			public bool areaIsTag;
			public int area;
			/// <summary>
			/// Number of triangle indices.
			/// Must be a multiple of 3.
			/// -1 indicates that all indices in the buffer should be used.
			/// </summary>
			public int indicesCount;

			/// <summary>
			/// If true then the mesh will be treated as solid and its interior will be unwalkable.
			/// The unwalkable region will be the minimum to maximum y coordinate in each cell.
			/// </summary>
			public bool solid;

			/// <summary>World bounds of the mesh. Assumed to already be multiplied with the matrix</summary>
			public Bounds bounds;

			public Matrix4x4 matrix;

			public void RecalculateBounds () {
				// This will cause the bounds to be recalculated later
				bounds = new Bounds();
			}
		}

		enum MeshType {
			Mesh,
			Box,
			Capsule,
		}

		struct MeshCacheItem : IEquatable<MeshCacheItem> {
			public MeshType type;
			public Mesh mesh;
			public int rows;
			public int quantizedHeight;

			public MeshCacheItem (Mesh mesh) {
				type = MeshType.Mesh;
				this.mesh = mesh;
				rows = 0;
				quantizedHeight = 0;
			}

			public static readonly MeshCacheItem Box = new MeshCacheItem {
				type = MeshType.Box,
				mesh = null,
				rows = 0,
				quantizedHeight = 0,
			};

			public bool Equals (MeshCacheItem other) {
				return type == other.type && mesh == other.mesh && rows == other.rows && quantizedHeight == other.quantizedHeight;
			}

			public override int GetHashCode () {
				return (((int)type * 31 ^ (mesh != null ? mesh.GetHashCode() : -1)) * 31 ^ rows) * 31 ^ quantizedHeight;
			}
		}

		bool MeshFilterShouldBeIncluded (MeshFilter filter) {
			if (filter.TryGetComponent<Renderer>(out Renderer rend)) {
				if (filter.sharedMesh != null && rend.enabled && (((1 << filter.gameObject.layer) & mask) != 0 || tagMask.Contains(filter.tag))) {
					if (!(filter.TryGetComponent<RecastMeshObj>(out RecastMeshObj rmo) && rmo.enabled)) {
						return true;
					}
				}
			}
			return false;
		}

		void AddNewMesh (Renderer renderer, Mesh mesh, int area, bool solid = false, bool areaIsTag = false) {
			// Ignore meshes that do not have a Position vertex attribute.
			// This can happen for meshes that are empty, i.e. have no vertices at all.
			if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position)) {
				return;
			}

#if !UNITY_EDITOR
			if (!mesh.isReadable) {
				// Cannot scan this
				if (!anyNonReadableMesh) {
					Debug.LogError("Some meshes could not be included when scanning the graph because they are marked as not readable. This includes the mesh '" + mesh.name + "'. You need to mark the mesh with read/write enabled in the mesh importer. Alternatively you can only rasterize colliders and not meshes. Mesh Collider meshes still need to be readable.", mesh);
				}
				anyNonReadableMesh = true;
				return;
			}
#endif

			// Check the cache to avoid allocating
			// a new array unless necessary
			int meshBufferIndex;

			if (!cachedMeshes.TryGetValue(new MeshCacheItem(mesh), out meshBufferIndex)) {
#if UNITY_EDITOR
				if (!mesh.isReadable) meshesUnreadableAtRuntime.Add(mesh);
#endif
				meshBufferIndex = meshData.Count;
				meshData.Add(mesh);
				cachedMeshes[new MeshCacheItem(mesh)] = meshBufferIndex;
			}

			meshes.Add(new GatheredMesh {
				meshDataIndex = meshBufferIndex,
				bounds = renderer.bounds,
				indicesCount = -1,
				areaIsTag = areaIsTag,
				area = area,
				solid = solid,
				matrix = renderer.localToWorldMatrix,
			});
		}

		GatheredMesh? GetColliderMesh (MeshCollider collider, Matrix4x4 localToWorldMatrix) {
			if (collider.sharedMesh != null) {
				Mesh mesh = collider.sharedMesh;

				// Ignore meshes that do not have a Position vertex attribute.
				// This can happen for meshes that are empty, i.e. have no vertices at all.
				if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position)) {
					return null;
				}

#if !UNITY_EDITOR
				if (!mesh.isReadable) {
					// Cannot scan this
					if (!anyNonReadableMesh) {
						Debug.LogError("Some mesh collider meshes could not be included when scanning the graph because they are marked as not readable. This includes the mesh '" + mesh.name + "'. You need to mark the mesh with read/write enabled in the mesh importer.", mesh);
					}
					anyNonReadableMesh = true;
					return null;
				}
#endif

				// Check the cache to avoid allocating
				// a new array unless necessary
				int meshDataIndex;
				if (!cachedMeshes.TryGetValue(new MeshCacheItem(mesh), out meshDataIndex)) {
#if UNITY_EDITOR
					if (!mesh.isReadable) meshesUnreadableAtRuntime.Add(mesh);
#endif
					meshDataIndex = meshData.Count;
					meshData.Add(mesh);
					cachedMeshes[new MeshCacheItem(mesh)] = meshDataIndex;
				}

				return new GatheredMesh {
						   meshDataIndex = meshDataIndex,
						   bounds = collider.bounds,
						   areaIsTag = false,
						   area = 0,
						   indicesCount = -1,
						   // Treat the collider as solid iff the collider is convex
						   solid = collider.convex,
						   matrix = localToWorldMatrix,
				};
			}

			return null;
		}

		public void CollectSceneMeshes () {
			if (tagMask.Count > 0 || mask != 0) {
				// This is unfortunately the fastest way to find all mesh filters.. and it is not particularly fast.
				var meshFilters = GameObject.FindObjectsOfType<MeshFilter>();
				bool containedStatic = false;

				for (int i = 0; i < meshFilters.Length; i++) {
					MeshFilter filter = meshFilters[i];

					if (!MeshFilterShouldBeIncluded(filter)) continue;

					// Note, guaranteed to have a renderer
					filter.TryGetComponent<Renderer>(out Renderer rend);

					if (rend.isPartOfStaticBatch) {
						// Statically batched meshes cannot be used due to Unity limitations
						// log a warning about this
						containedStatic = true;
					} else {
						// Only include it if it intersects with the graph
						if (rend.bounds.Intersects(bounds)) {
							Mesh mesh = filter.sharedMesh;
							AddNewMesh(rend, filter.sharedMesh, 0);
						}
					}

					if (containedStatic)
						Debug.LogWarning("Some meshes were statically batched. These meshes can not be used for navmesh calculation" +
							" due to technical constraints.\nDuring runtime scripts cannot access the data of meshes which have been statically batched.\n" +
							"One way to solve this problem is to use cached startup (Save & Load tab in the inspector) to only calculate the graph when the game is not playing.");
				}
			}
		}

		static int RecastAreaFromRecastMeshObj (RecastMeshObj obj) {
			switch (obj.mode) {
			default:
			case RecastMeshObj.Mode.ExcludeFromGraph:
				throw new System.Exception("Should not have reached this point");
			case RecastMeshObj.Mode.UnwalkableSurface:
				return -1;
			case RecastMeshObj.Mode.WalkableSurface:
				return 0;
			case RecastMeshObj.Mode.WalkableSurfaceWithSeam:
			case RecastMeshObj.Mode.WalkableSurfaceWithTag:
				return obj.surfaceID;
			}
		}

		/// <summary>Find all relevant RecastMeshObj components and create ExtraMeshes for them</summary>
		public void CollectRecastMeshObjs () {
			var buffer = Util.ListPool<RecastMeshObj>.Claim();

			// Get all recast mesh objects inside the bounds
			RecastMeshObj.GetAllInBounds(buffer, bounds);

			// Create an RasterizationMesh object
			// for each RecastMeshObj
			for (int i = 0; i < buffer.Count; i++) {
				if (buffer[i].mode == RecastMeshObj.Mode.ExcludeFromGraph) continue;

				MeshFilter filter = buffer[i].GetMeshFilter();

				if (filter != null && filter.TryGetComponent<Renderer>(out Renderer rend) && filter.sharedMesh != null) {
					// Add based on mesh filter
					Mesh mesh = filter.sharedMesh;
					AddNewMesh(rend, filter.sharedMesh, RecastAreaFromRecastMeshObj(buffer[i]), buffer[i].solid, buffer[i].mode == RecastMeshObj.Mode.WalkableSurfaceWithTag);
				} else {
					// Add based on collider
					Collider coll = buffer[i].GetCollider();

					if (coll == null) {
						Debug.LogError("RecastMeshObject ("+buffer[i].gameObject.name +") didn't have a collider or MeshFilter+Renderer attached", buffer[i].gameObject);
						continue;
					}

					if (GetColliderMesh(coll) is GatheredMesh rmesh) {
						rmesh.area = RecastAreaFromRecastMeshObj(buffer[i]);
						rmesh.areaIsTag = buffer[i].mode == RecastMeshObj.Mode.WalkableSurfaceWithTag;
						rmesh.solid |= buffer[i].solid;
						meshes.Add(rmesh);
					}
				}
			}

			Util.ListPool<RecastMeshObj>.Release(ref buffer);
		}

		public void CollectTerrainMeshes (bool rasterizeTrees, float desiredChunkSize) {
			// Find all terrains in the scene
			var terrains = Terrain.activeTerrains;

			if (terrains.Length > 0) {
				// Loop through all terrains in the scene
				for (int j = 0; j < terrains.Length; j++) {
					if (terrains[j].terrainData == null) continue;

					GenerateTerrainChunks(terrains[j], bounds, desiredChunkSize);

					if (rasterizeTrees) {
						// Rasterize all tree colliders on this terrain object
						CollectTreeMeshes(terrains[j]);
					}
				}
			}
		}

		void GenerateTerrainChunks (Terrain terrain, Bounds bounds, float desiredChunkSize) {
			var terrainData = terrain.terrainData;

			if (terrainData == null)
				throw new System.ArgumentException("Terrain contains no terrain data");

			Vector3 offset = terrain.GetPosition();
			Vector3 center = offset + terrainData.size * 0.5F;

			// Figure out the bounds of the terrain in world space
			var terrainBounds = new Bounds(center, terrainData.size);

			// Only include terrains which intersects the graph
			if (!terrainBounds.Intersects(bounds))
				return;

			// Original heightmap size
			int heightmapWidth = terrainData.heightmapResolution;
			int heightmapDepth = terrainData.heightmapResolution;

			// Sample the terrain heightmap
			float[, ] heights = terrainData.GetHeights(0, 0, heightmapWidth, heightmapDepth);
			bool[, ] holes = terrainData.GetHoles(0, 0, heightmapWidth-1, heightmapDepth-1);

			Vector3 sampleSize = terrainData.heightmapScale;
			sampleSize.y = terrainData.size.y;

			// Make chunks at least 12 quads wide
			// since too small chunks just decreases performance due
			// to the overhead of checking for bounds and similar things
			const int MinChunkSize = 12;

			// Find the number of samples along each edge that corresponds to a world size of desiredChunkSize
			// Then round up to the nearest multiple of terrainSampleSize
			var chunkSizeAlongX = Mathf.CeilToInt(Mathf.Max(desiredChunkSize / (sampleSize.x * terrainSampleSize), MinChunkSize)) * terrainSampleSize;
			var chunkSizeAlongZ = Mathf.CeilToInt(Mathf.Max(desiredChunkSize / (sampleSize.z * terrainSampleSize), MinChunkSize)) * terrainSampleSize;

			for (int z = 0; z < heightmapDepth; z += chunkSizeAlongZ) {
				for (int x = 0; x < heightmapWidth; x += chunkSizeAlongX) {
					var width = Mathf.Min(chunkSizeAlongX, heightmapWidth - x);
					var depth = Mathf.Min(chunkSizeAlongZ, heightmapDepth - z);
					var chunkMin = offset + new Vector3(z * sampleSize.x, 0, x * sampleSize.z);
					var chunkMax = offset + new Vector3((z + depth) * sampleSize.x, sampleSize.y, (x + width) * sampleSize.z);
					var chunkBounds = new Bounds();
					chunkBounds.SetMinMax(chunkMin, chunkMax);

					// Skip chunks that are not inside the desired bounds
					if (chunkBounds.Intersects(bounds)) {
						var chunk = GenerateHeightmapChunk(heights, holes, sampleSize, offset, x, z, width, depth, terrainSampleSize);
						meshes.Add(chunk);
					}
				}
			}
		}

		/// <summary>Returns ceil(lhs/rhs), i.e lhs/rhs rounded up</summary>
		static int CeilDivision (int lhs, int rhs) {
			return (lhs + rhs - 1)/rhs;
		}

		/// <summary>Generates a terrain chunk mesh</summary>
		GatheredMesh GenerateHeightmapChunk (float[, ] heights, bool[,] holes, Vector3 sampleSize, Vector3 offset, int x0, int z0, int width, int depth, int stride) {
			// Downsample to a smaller mesh (full resolution will take a long time to rasterize)
			// Round up the width to the nearest multiple of terrainSampleSize and then add 1
			// (off by one because there are vertices at the edge of the mesh)
			int resultWidth = CeilDivision(width, terrainSampleSize) + 1;
			int resultDepth = CeilDivision(depth, terrainSampleSize) + 1;

			var heightmapWidth = heights.GetLength(0);
			var heightmapDepth = heights.GetLength(1);

			// Create a mesh from the heightmap
			var numVerts = resultWidth * resultDepth;
			var verts = new NativeArray<Vector3>(numVerts, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

			int numTris = (resultWidth-1)*(resultDepth-1)*2*3;
			var tris = new NativeArray<int>(numTris, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

			// Create lots of vertices
			for (int z = 0; z < resultDepth; z++) {
				for (int x = 0; x < resultWidth; x++) {
					int sampleX = Math.Min(x0 + x*stride, heightmapWidth-1);
					int sampleZ = Math.Min(z0 + z*stride, heightmapDepth-1);

					verts[z*resultWidth + x] = new Vector3(sampleZ * sampleSize.x, heights[sampleX, sampleZ]*sampleSize.y, sampleX * sampleSize.z) + offset;
				}
			}

			// Create the mesh by creating triangles in a grid like pattern
			int triangleIndex = 0;
			for (int z = 0; z < resultDepth-1; z++) {
				for (int x = 0; x < resultWidth-1; x++) {
					// Try to check if the center of the cell is a hole or not.
					// Note that the holes array has a size which is 1 less than the heightmap size
					int sampleX = Math.Min(x0 + stride/2 + x*stride, heightmapWidth-2);
					int sampleZ = Math.Min(z0 + stride/2 + z*stride, heightmapDepth-2);

					if (holes[sampleX, sampleZ]) {
						// Note a hole, generate a mesh here
						tris[triangleIndex]   = z*resultWidth + x;
						tris[triangleIndex+1] = z*resultWidth + x+1;
						tris[triangleIndex+2] = (z+1)*resultWidth + x+1;
						triangleIndex += 3;
						tris[triangleIndex]   = z*resultWidth + x;
						tris[triangleIndex+1] = (z+1)*resultWidth + x+1;
						tris[triangleIndex+2] = (z+1)*resultWidth + x;
						triangleIndex += 3;
					}
				}
			}


			var meshDataIndex = AddMeshBuffers(verts, tris);

			var mesh = new GatheredMesh {
				meshDataIndex = meshDataIndex,
				// An empty bounding box indicates that it should be calculated from the vertices later.
				bounds = new Bounds(),
				indicesCount = triangleIndex,
				areaIsTag = false,
				area = 0,
				solid = false,
				matrix = Matrix4x4.identity,
			};
			return mesh;
		}

		void CollectTreeMeshes (Terrain terrain) {
			TerrainData data = terrain.terrainData;
			var treeInstances = data.treeInstances;
			var treePrototypes = data.treePrototypes;

			for (int i = 0; i < treeInstances.Length; i++) {
				TreeInstance instance = treeInstances[i];
				TreePrototype prot = treePrototypes[instance.prototypeIndex];

				// Make sure that the tree prefab exists
				if (prot.prefab == null) {
					continue;
				}

				TreeInfo treeInfo;
				if (!cachedTreePrefabs.TryGetValue(prot.prefab, out treeInfo)) {
					treeInfo.submeshes = new List<GatheredMesh>();

					// The unity terrain system only supports rotation for trees with a LODGroup on the root object.
					// Unity still sets the instance.rotation field to values even they are not used, so we need to explicitly check for this.
					LODGroup dummy;
					treeInfo.supportsRotation = prot.prefab.TryGetComponent<LODGroup>(out dummy);

					var colliders = ListPool<Collider>.Claim();
					var rootMatrixInv = prot.prefab.transform.localToWorldMatrix.inverse;
					prot.prefab.GetComponentsInChildren<Collider>(false, colliders);
					for (int j = 0; j < colliders.Count; j++) {
						// The prefab has a collider, use that instead

						// Generate a mesh from the collider
						if (GetColliderMesh(colliders[j], rootMatrixInv * colliders[j].transform.localToWorldMatrix) is GatheredMesh mesh) {
							if (colliders[j].gameObject.TryGetComponent<RecastMeshObj>(out RecastMeshObj recastMeshObj) && recastMeshObj.enabled) {
								if (recastMeshObj.mode == RecastMeshObj.Mode.ExcludeFromGraph) continue;

								mesh.area = RecastAreaFromRecastMeshObj(recastMeshObj);
								mesh.solid |= recastMeshObj.solid;
								mesh.areaIsTag = recastMeshObj.mode == RecastMeshObj.Mode.WalkableSurfaceWithTag;
							}

							// The bounds are incorrectly based on collider.bounds.
							// It is incorrect because the collider is on the prefab, not on the tree instance
							// so we need to recalculate the bounds based on the actual vertex positions
							mesh.RecalculateBounds();
							//mesh.matrix = collider.transform.localToWorldMatrix.inverse * mesh.matrix;
							treeInfo.submeshes.Add(mesh);
						}
					}

					ListPool<Collider>.Release(ref colliders);
					cachedTreePrefabs[prot.prefab] = treeInfo;
				}

				var treePosition = terrain.transform.position +  Vector3.Scale(instance.position, data.size);
				var instanceSize = new Vector3(instance.widthScale, instance.heightScale, instance.widthScale);
				var prefabScale = Vector3.Scale(instanceSize, prot.prefab.transform.localScale);
				var rotation = treeInfo.supportsRotation ? instance.rotation : 0;
				var matrix = Matrix4x4.TRS(treePosition, Quaternion.AngleAxis(rotation * Mathf.Rad2Deg, Vector3.up), prefabScale);

				for (int j = 0; j < treeInfo.submeshes.Count; j++) {
					var item = treeInfo.submeshes[j];
					item.matrix = matrix * item.matrix;
					meshes.Add(item);
				}
			}
		}

		bool ShouldIncludeCollider (Collider collider) {
			return (((mask >> collider.gameObject.layer) & 1) != 0 || tagMask.Contains(collider.tag)) && collider.enabled && !collider.isTrigger && collider.bounds.Intersects(bounds) && !(collider.TryGetComponent<RecastMeshObj>(out RecastMeshObj rmo) && rmo.enabled);
		}

		public void CollectColliderMeshes () {
			if (tagMask.Count == 0 && mask == 0) return;

			var physicsScene = scene.GetPhysicsScene();
			// Find all colliders that could possibly be inside the bounds
			// TODO: Benchmark?
			// Repeatedly do a OverlapBox check and make the buffer larger if it's too small.
			int numColliders = 64;
			Collider[] colliderBuffer = null;
			do {
				if (colliderBuffer != null) Util.ArrayPool<Collider>.Release(ref colliderBuffer);
				colliderBuffer = Util.ArrayPool<Collider>.Claim(numColliders * 4);
				numColliders = physicsScene.OverlapBox(bounds.center, bounds.extents, colliderBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
			} while (numColliders == colliderBuffer.Length);


			for (int i = 0; i < numColliders; i++) {
				Collider collider = colliderBuffer[i];

				if (ShouldIncludeCollider(collider)) {
					if (GetColliderMesh(collider) is GatheredMesh mesh) {
						meshes.Add(mesh);
					}
				}
			}

			Util.ArrayPool<Collider>.Release(ref colliderBuffer);
		}

		/// <summary>
		/// Box Collider triangle indices can be reused for multiple instances.
		/// Warning: This array should never be changed
		/// </summary>
		private readonly static int[] BoxColliderTris = {
			0, 1, 2,
			0, 2, 3,

			6, 5, 4,
			7, 6, 4,

			0, 5, 1,
			0, 4, 5,

			1, 6, 2,
			1, 5, 6,

			2, 7, 3,
			2, 6, 7,

			3, 4, 0,
			3, 7, 4
		};

		/// <summary>
		/// Box Collider vertices can be reused for multiple instances.
		/// Warning: This array should never be changed
		/// </summary>
		private readonly static Vector3[] BoxColliderVerts = {
			new Vector3(-1, -1, -1),
			new Vector3(1, -1, -1),
			new Vector3(1, -1, 1),
			new Vector3(-1, -1, 1),

			new Vector3(-1, 1, -1),
			new Vector3(1, 1, -1),
			new Vector3(1, 1, 1),
			new Vector3(-1, 1, 1),
		};

		/// <summary>
		/// Rasterizes a collider to a mesh.
		/// This will pass the col.transform.localToWorldMatrix to the other overload of this function.
		/// </summary>
		GatheredMesh? GetColliderMesh (Collider col) {
			return GetColliderMesh(col, col.transform.localToWorldMatrix);
		}

		/// <summary>
		/// Rasterizes a collider to a mesh assuming it's vertices should be multiplied with the matrix.
		/// Note that the bounds of the returned RasterizationMesh is based on collider.bounds. So you might want to
		/// call myExtraMesh.RecalculateBounds on the returned mesh to recalculate it if the collider.bounds would
		/// not give the correct value.
		/// </summary>
		GatheredMesh? GetColliderMesh (Collider col, Matrix4x4 localToWorldMatrix) {
			if (col is BoxCollider box) {
				return RasterizeBoxCollider(box, localToWorldMatrix);
			} else if (col is SphereCollider || col is CapsuleCollider) {
				var scollider = col as SphereCollider;
				var ccollider = col as CapsuleCollider;

				float radius = (scollider != null ? scollider.radius : ccollider.radius);
				float height = scollider != null ? 0 : (ccollider.height*0.5f/radius) - 1;
				Quaternion rot = Quaternion.identity;
				// Capsule colliders can be aligned along the X, Y or Z axis
				if (ccollider != null) rot = Quaternion.Euler(ccollider.direction == 2 ? 90 : 0, 0, ccollider.direction == 0 ? 90 : 0);
				Matrix4x4 matrix = Matrix4x4.TRS(scollider != null ? scollider.center : ccollider.center, rot, Vector3.one*radius);

				matrix = localToWorldMatrix * matrix;

				return RasterizeCapsuleCollider(radius, height, col.bounds, matrix);
			} else if (col is MeshCollider collider) {
				return GetColliderMesh(collider, localToWorldMatrix);
			}

			return null;
		}

		GatheredMesh RasterizeBoxCollider (BoxCollider collider, Matrix4x4 localToWorldMatrix) {
			Matrix4x4 matrix = Matrix4x4.TRS(collider.center, Quaternion.identity, collider.size*0.5f);

			matrix = localToWorldMatrix * matrix;

			int meshDataIndex;
			if (!cachedMeshes.TryGetValue(MeshCacheItem.Box, out meshDataIndex)) {
				meshDataIndex = AddMeshBuffers(BoxColliderVerts, BoxColliderTris);
				cachedMeshes[MeshCacheItem.Box] = meshDataIndex;
			}

			return new GatheredMesh {
					   meshDataIndex = meshDataIndex,
					   bounds = collider.bounds,
					   indicesCount = -1,
					   areaIsTag = false,
					   area = 0,
					   solid = true,
					   matrix = matrix,
			};
		}

		GatheredMesh RasterizeCapsuleCollider (float radius, float height, Bounds bounds, Matrix4x4 localToWorldMatrix) {
			// Calculate the number of rows to use
			// grows as sqrt(x) to the radius of the sphere/capsule which I have found works quite well
			int rows = Mathf.Max(4, Mathf.RoundToInt(colliderRasterizeDetail*Mathf.Sqrt(localToWorldMatrix.MultiplyVector(Vector3.one).magnitude)));

			if (rows > 100) {
				Debug.LogWarning("Very large detail for some collider meshes. Consider decreasing Collider Rasterize Detail (RecastGraph)");
			}

			int cols = rows;

			var cacheItem = new MeshCacheItem {
				type = MeshType.Capsule,
				mesh = null,
				rows = rows,
				// Capsules that differ by less than 0.01 units in height will be rasterized the same
				quantizedHeight = Mathf.RoundToInt(height*100),
			};

			int meshDataIndex;
			if (!cachedMeshes.TryGetValue(cacheItem, out meshDataIndex)) {
				// Generate a sphere/capsule mesh

				var verts = new NativeArray<Vector3>(rows*cols + 2, Allocator.Persistent);

				var tris = new NativeArray<int>(rows*cols*2*3, Allocator.Persistent);

				for (int r = 0; r < rows; r++) {
					for (int c = 0; c < cols; c++) {
						verts[c + r*cols] = new Vector3(Mathf.Cos(c*Mathf.PI*2/cols)*Mathf.Sin((r*Mathf.PI/(rows-1))), Mathf.Cos((r*Mathf.PI/(rows-1))) + (r < rows/2 ? height : -height), Mathf.Sin(c*Mathf.PI*2/cols)*Mathf.Sin((r*Mathf.PI/(rows-1))));
					}
				}

				verts[verts.Length-1] = Vector3.up;
				verts[verts.Length-2] = Vector3.down;

				int triIndex = 0;

				for (int i = 0, j = cols-1; i < cols; j = i++) {
					tris[triIndex + 0] = (verts.Length-1);
					tris[triIndex + 1] = (0*cols + j);
					tris[triIndex + 2] = (0*cols + i);
					triIndex += 3;
				}

				for (int r = 1; r < rows; r++) {
					for (int i = 0, j = cols-1; i < cols; j = i++) {
						tris[triIndex + 0] = (r*cols + i);
						tris[triIndex + 1] = (r*cols + j);
						tris[triIndex + 2] = ((r-1)*cols + i);
						triIndex += 3;

						tris[triIndex + 0] = ((r-1)*cols + j);
						tris[triIndex + 1] = ((r-1)*cols + i);
						tris[triIndex + 2] = (r*cols + j);
						triIndex += 3;
					}
				}

				for (int i = 0, j = cols-1; i < cols; j = i++) {
					tris[triIndex + 0] = (verts.Length-2);
					tris[triIndex + 1] = ((rows-1)*cols + j);
					tris[triIndex + 2] = ((rows-1)*cols + i);
					triIndex += 3;
				}

				UnityEngine.Assertions.Assert.AreEqual(triIndex, tris.Length);

				// TOOD: Avoid allocating original C# array
				// Store custom vertex buffers as negative indices
				meshDataIndex = AddMeshBuffers(verts, tris);
				cachedMeshes[cacheItem] = meshDataIndex;
			}

			return new GatheredMesh {
					   meshDataIndex = meshDataIndex,
					   bounds = bounds,
					   areaIsTag = false,
					   area = 0,
					   indicesCount = -1,
					   solid = true,
					   matrix = localToWorldMatrix,
			};
		}
	}
}
