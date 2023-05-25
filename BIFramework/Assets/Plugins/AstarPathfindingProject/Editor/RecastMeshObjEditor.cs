using UnityEngine;
using UnityEditor;

namespace Pathfinding {
	[CustomEditor(typeof(RecastMeshObj))]
	[CanEditMultipleObjects]
	public class RecastMeshObjEditor : EditorBase {
		protected override void Inspector () {
			var modeProp = FindProperty("mode");
			var areaProp = FindProperty("surfaceID");

			PropertyField(modeProp, "Surface Type");
			var mode = (RecastMeshObj.Mode)modeProp.enumValueIndex;

			if (areaProp.intValue < 0) {
				areaProp.intValue = 0;
			}

			if (!modeProp.hasMultipleDifferentValues) {
				switch (mode) {
				case RecastMeshObj.Mode.ExcludeFromGraph:
					EditorGUILayout.HelpBox("This object will be completely ignored by the graph. Even if it would otherwise be included due to its layer or tag.", MessageType.None);
					break;
				case RecastMeshObj.Mode.UnwalkableSurface:
					EditorGUILayout.HelpBox("All surfaces on this mesh will be made unwalkable", MessageType.None);
					break;
				case RecastMeshObj.Mode.WalkableSurface:
					EditorGUILayout.HelpBox("All surfaces on this mesh will be walkable", MessageType.None);
					break;
				case RecastMeshObj.Mode.WalkableSurfaceWithSeam:
					EditorGUILayout.HelpBox("All surfaces on this mesh will be walkable and a " +
						"seam will be created between the surfaces on this mesh and the surfaces on other meshes (with a different surface id)", MessageType.None);
					EditorGUI.indentLevel++;
					PropertyField(areaProp, "Surface ID");
					EditorGUI.indentLevel--;
					break;
				case RecastMeshObj.Mode.WalkableSurfaceWithTag:
					EditorGUILayout.HelpBox("All surfaces on this mesh will be walkable and the given tag will be applied to them", MessageType.None);
					EditorGUI.indentLevel++;

					EditorGUI.showMixedValue = areaProp.hasMultipleDifferentValues;
					EditorGUI.BeginChangeCheck();
					var newTag = Util.EditorGUILayoutHelper.TagField("Tag Value", areaProp.intValue, () => AstarPathEditor.EditTags());
					if (EditorGUI.EndChangeCheck()) {
						areaProp.intValue = newTag;
					}
					if (areaProp.intValue < 0 || areaProp.intValue > GraphNode.MaxTagIndex) {
						areaProp.intValue = Mathf.Clamp(areaProp.intValue, 0, GraphNode.MaxTagIndex);
					}

					EditorGUI.indentLevel--;
					break;
				}
			}

			var dynamicProp = FindProperty("dynamic");
			PropertyField(dynamicProp, "Dynamic", "Setting this value to false will give better scanning performance, but you will not be able to move the object during runtime");
			if (!dynamicProp.hasMultipleDifferentValues && !dynamicProp.boolValue) {
				EditorGUILayout.HelpBox("This object must not be moved during runtime since 'dynamic' is set to false", MessageType.Info);
			}

			if (this.targets.Length == 1) {
				var script = target as RecastMeshObj;
				MeshFilter filter = script.GetMeshFilter();
				Collider collider = script.GetCollider();
				bool useMeshFilter = filter != null && filter.TryGetComponent<Renderer>(out Renderer rend) && filter.sharedMesh != null;
				bool colliderIsConvex = collider != null && (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider || (collider is MeshCollider mc && mc.convex));
				if (!useMeshFilter && colliderIsConvex) {
					// Forced solid
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.Toggle("Solid", true);
					EditorGUILayout.HelpBox("Convex colliders are always treated as solid", MessageType.Info);
					EditorGUI.EndDisabledGroup();
				} else {
					PropertyField("solid");
				}
			}
		}
	}
}
