using UnityEngine;
using System.Collections.Generic;

namespace Pathfinding {
	/// <summary>
	/// Movement script for curved worlds.
	/// This script inherits from AIPath, but adjusts its movement plane every frame using the ground normal.
	/// </summary>
	public class AIPathAlignedToSurface : AIPath {
		protected override void Start () {
			base.Start();
			movementPlane = new Util.SimpleMovementPlane(rotation);
		}

		protected override void OnUpdate (float dt) {
			base.OnUpdate(dt);
			UpdateMovementPlane();
		}

		protected override void ApplyGravity (float deltaTime) {
			// Apply gravity
			if (usingGravity) {
				// Gravity is relative to the current surface.
				// Only the normal direction is well defined however so x and z are ignored.
				verticalVelocity += float.IsNaN(gravity.x) ? Physics.gravity.y : gravity.y;
			} else {
				verticalVelocity = 0;
			}
		}

		Mesh cachedMesh;
		List<Vector3> cachedNormals = new List<Vector3>();
		List<int> cachedTriangles = new List<int>();
		int cachedVertexCount;

		/// <summary>
		/// Call to indicate that the mesh collider which the agent is standing on may have been updated.
		///
		/// Sadly, there is no way in Unity to efficiently check if a mesh has been updated.
		/// This script will check the vertex count of the mesh and if it has changed, it will get the normals from the mesh.
		/// This is not always accurate, however: the mesh could have changed without its vertex count changing.
		/// So you may want to call this method if you know that the mesh has been updated.
		///
		/// Calling this will cost a small bit of overhead in the next movement update loop as the script gets the normals from the mesh again.
		/// </summary>
		public void MeshBelowFeetMayHaveChanged () {
			cachedMesh = null;
		}

		Vector3 InterpolateNormal (RaycastHit hit) {
			MeshCollider meshCollider = hit.collider as MeshCollider;

			if (meshCollider == null || meshCollider.sharedMesh == null)
				return hit.normal;

			Mesh mesh = meshCollider.sharedMesh;

			// For performance, cache the triangles and normals from the last frame
			if (mesh != cachedMesh || mesh.vertexCount != cachedVertexCount) {
				if (!mesh.isReadable) return hit.normal;
				cachedMesh = mesh;
				cachedVertexCount = mesh.vertexCount;
				mesh.GetNormals(cachedNormals);
				if (mesh.subMeshCount == 1) {
					mesh.GetTriangles(cachedTriangles, 0);
				} else {
					List<int> buffer = Util.ListPool<int>.Claim();
					// Absolutely horrible, but there doesn't seem to be another way to do this without allocating a ton of memory each time
					for (int i = 0; i < mesh.subMeshCount; i++) {
						mesh.GetTriangles(buffer, i);
						cachedTriangles.AddRange(buffer);
					}
					Util.ListPool<int>.Release(ref buffer);
				}
			}

			var normals = cachedNormals;
			var triangles = cachedTriangles;
			Vector3 n0 = normals[triangles[hit.triangleIndex * 3 + 0]];
			Vector3 n1 = normals[triangles[hit.triangleIndex * 3 + 1]];
			Vector3 n2 = normals[triangles[hit.triangleIndex * 3 + 2]];
			Vector3 baryCenter = hit.barycentricCoordinate;
			Vector3 interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;
			interpolatedNormal = interpolatedNormal.normalized;
			Transform hitTransform = hit.collider.transform;
			interpolatedNormal = hitTransform.TransformDirection(interpolatedNormal);
			return interpolatedNormal;
		}


		/// <summary>Find the world position of the ground below the character</summary>
		protected override void UpdateMovementPlane () {
			// Construct a new movement plane which has new normal
			// but is otherwise as similar to the previous plane as possible
			var normal = InterpolateNormal(lastRaycastHit);

			if (normal != Vector3.zero) {
				var fwd = Vector3.Cross(movementPlane.rotation * Vector3.right, normal);
				movementPlane = new Util.SimpleMovementPlane(Quaternion.LookRotation(fwd, normal));
			}
			if (rvoController != null) rvoController.movementPlane = movementPlane;
		}
	}
}
