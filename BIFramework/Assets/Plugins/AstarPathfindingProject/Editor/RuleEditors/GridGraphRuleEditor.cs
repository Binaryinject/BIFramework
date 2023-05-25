
namespace Pathfinding {
	public interface IGridGraphRuleEditor {
		void OnInspectorGUI(GridGraph graph, GridGraphRule rule);
		void OnSceneGUI(GridGraph graph, GridGraphRule rule);
	}
}
