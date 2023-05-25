using UnityEngine;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Burst;

namespace Pathfinding.Voxels {
	using Pathfinding.Util;

	/// <summary>Various utilities for voxel rasterization.</summary>
	public class Utility {
		public static float Min (float a, float b, float c) {
			a = a < b ? a : b;
			return a < c ? a : c;
		}

		public static float Max (float a, float b, float c) {
			a = a > b ? a : b;
			return a > c ? a : c;
		}

		/// <summary>
		/// Removes duplicate vertices from the array and updates the triangle array.
		/// Returns: The new array of vertices
		/// </summary>
		public static Int3[] RemoveDuplicateVertices (Int3[] vertices, int[] triangles) {
			for (int i = 0; i < triangles.Length; i++) {
				if (triangles[i] >= vertices.Length) {
					Debug.Log("Out of range triangle " + triangles[i] + " >= " + vertices.Length);
				}
			}
			// Get a dictionary from an object pool to avoid allocating a new one
			var firstVerts = ObjectPoolSimple<Dictionary<Int3, int> >.Claim();

			firstVerts.Clear();

			// Remove duplicate vertices
			var compressedPointers = new int[vertices.Length];

			int count = 0;
			for (int i = 0; i < vertices.Length; i++) {
				if (!firstVerts.ContainsKey(vertices[i])) {
					firstVerts.Add(vertices[i], count);
					compressedPointers[i] = count;
					vertices[count] = vertices[i];
					count++;
				} else {
					// There are some cases, rare but still there, that vertices are identical
					compressedPointers[i] = firstVerts[vertices[i]];
				}
			}

			firstVerts.Clear();
			ObjectPoolSimple<Dictionary<Int3, int> >.Release(ref firstVerts);

			for (int i = 0; i < triangles.Length; i++) {
				triangles[i] = compressedPointers[triangles[i]];
			}

			var compressed = new Int3[count];
			for (int i = 0; i < count; i++) compressed[i] = vertices[i];
			return compressed;
		}

		[BurstCompile(FloatMode = FloatMode.Fast)]
		public struct JobTransformTileCoordinates : IJob {
			// Element type Int3
			public unsafe UnsafeAppendBuffer* vertices;
			public Matrix4x4 matrix;

			public void Execute () {
				unsafe {
					int vertexCount = vertices->Length / UnsafeUtility.SizeOf<Int3>();
					for (int i = 0; i < vertexCount; i++) {
						// Transform from voxel indices to a proper Int3 coordinate, then convert it to a Vector3 float coordinate
						var vPtr1 = (Int3*)vertices->Ptr + i;
						var p = (Vector3)((*vPtr1) * Int3.Precision);
						*vPtr1 = (Int3)matrix.MultiplyPoint3x4(p);
					}
				}
			}
		}

		/// <summary>Convert recast region IDs to the tags that should be applied to the nodes</summary>
		[BurstCompile]
		public struct JobConvertAreasToTags : IJob {
			[ReadOnly]
			public NativeList<int> inputAreas;
			/// Element type uint
			public unsafe UnsafeAppendBuffer* outputTags;

			public void Execute () {
				unsafe {
					outputTags->Reset();
					bool first = false;
					for (int i = 0; i < inputAreas.Length; i++) {
						var area = inputAreas[i];
						uint tag;
						if ((area & Voxels.Burst.VoxelUtilityBurst.TagReg) != 0) {
							first |= true;
							// The user supplied IDs start at 1 because 0 is reserved for NotWalkable
							tag = (uint)(area & Voxels.Burst.VoxelUtilityBurst.TagRegMask) - 1;
						} else {
							tag = 0;
						}
						outputTags->Add(tag);
					}
				}
			}
		}
	}
}
