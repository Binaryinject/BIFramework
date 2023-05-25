using UnityEngine;
using UnityEditor;

namespace Pathfinding {
	[CustomGraphEditor(typeof(LayerGridGraph), "Layered Grid Graph")]
	public class LayerGridGraphEditor : GridGraphEditor {
		protected override void DrawJPS (GridGraph graph) {
			// No JPS for layered grid graph
		}

		protected override void DrawMiddleSection (GridGraph graph) {
			var layerGridGraph = graph as LayerGridGraph;

			DrawNeighbours(graph);

			layerGridGraph.characterHeight = EditorGUILayout.DelayedFloatField("Character Height", layerGridGraph.characterHeight);
			DrawMaxClimb(graph);

			DrawMaxSlope(graph);
			DrawErosion(graph);
		}

		protected override void DrawMaxClimb (GridGraph graph) {
			var layerGridGraph = graph as LayerGridGraph;

			base.DrawMaxClimb(graph);
			layerGridGraph.maxStepHeight = Mathf.Clamp(layerGridGraph.maxStepHeight, 0, layerGridGraph.characterHeight);

			if (layerGridGraph.maxStepHeight >= layerGridGraph.characterHeight) {
				EditorGUILayout.HelpBox("Max step height needs to be smaller or equal to character height", MessageType.Info);
			}
		}

		protected override void DrawCollisionEditor (GraphCollision collision) {
			base.DrawCollisionEditor(collision);

			if (collision.thickRaycast) {
				EditorGUILayout.HelpBox("Note: Thick raycast cannot be used with this graph type", MessageType.Error);
			}
		}

		protected override void DrawUse2DPhysics (GraphCollision collision) {
			// 2D physics does not make sense for a layered grid graph
			collision.use2D = false;
		}
	}
}
