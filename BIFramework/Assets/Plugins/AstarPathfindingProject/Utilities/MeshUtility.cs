using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Pathfinding.Util {
	/// <summary>Helper class for working with meshes efficiently</summary>
	static class MeshUtility {
		public static void GetMeshData (Mesh.MeshDataArray meshData, int meshIndex, out NativeArray<Vector3> vertices, out NativeArray<int> indices) {
			var rawMeshData = meshData[meshIndex];
			vertices = new NativeArray<Vector3>(rawMeshData.vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			rawMeshData.GetVertices(vertices);
			int totalIndices = 0;
			for (int subMeshIndex = 0; subMeshIndex < rawMeshData.subMeshCount; subMeshIndex++) {
				totalIndices += rawMeshData.GetSubMesh(subMeshIndex).indexCount;
			}
			indices = new NativeArray<int>(totalIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			int offset = 0;
			for (int subMeshIndex = 0; subMeshIndex < rawMeshData.subMeshCount; subMeshIndex++) {
				var submesh = rawMeshData.GetSubMesh(subMeshIndex);
				rawMeshData.GetIndices(indices.GetSubArray(offset, submesh.indexCount), subMeshIndex);
				offset += submesh.indexCount;
			}
		}

		/// <summary>Removes duplicate vertices from the array and updates the triangle array.</summary>
		[BurstCompile]
		public struct JobRemoveDuplicateVertices : IJob {
			[ReadOnly]
			public NativeArray<Int3> vertices;
			[ReadOnly]
			public NativeArray<int> triangles;
			public unsafe UnsafeAppendBuffer* outputVertices; // Element Type Int3
			public unsafe UnsafeAppendBuffer* outputTriangles; // Element Type int

			public void Execute () {
				unsafe {
					outputVertices->Reset();
					outputTriangles->Reset();

					var firstVerts = new NativeParallelHashMap<Int3, int>(vertices.Length, Allocator.Temp);

					// Remove duplicate vertices
					var compressedPointers = new NativeArray<int>(vertices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

					int count = 0;

					for (int i = 0; i < vertices.Length; i++) {
						if (firstVerts.TryAdd(vertices[i], count)) {
							compressedPointers[i] = count;
							outputVertices->Add(vertices[i]);
							count++;
						} else {
							// There are some cases, rare but still there, that vertices are identical
							compressedPointers[i] = firstVerts[vertices[i]];
						}
					}

					for (int i = 0; i < triangles.Length; i++) {
						outputTriangles->Add(compressedPointers[triangles[i]]);
					}
				}
			}
		}
	}
}
