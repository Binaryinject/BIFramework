using UnityEditor;
using Pathfinding.RVO;

namespace Pathfinding {
	[CustomEditor(typeof(RVOSquareObstacle))]
	[CanEditMultipleObjects]
	public class RVOSquareObstacleEditor : Editor {
		public override void OnInspectorGUI () {
			EditorGUILayout.HelpBox("This component is deprecated. Local avoidance colliders never worked particularly well and the performance was poor. Modify the graphs instead so that pathfinding takes obstacles into account.", MessageType.Warning);
		}
	}
}
