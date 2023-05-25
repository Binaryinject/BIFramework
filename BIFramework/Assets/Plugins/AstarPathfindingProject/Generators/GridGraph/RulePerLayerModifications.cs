using System.Collections.Generic;

namespace Pathfinding {
	using Pathfinding.Jobs;
	using Unity.Jobs;
	using Unity.Collections;
	using Unity.Burst;
	using UnityEngine;
	using Unity.Mathematics;
	using Unity.Collections.LowLevel.Unsafe;

	/// <summary>
	/// Modifies nodes based on the layer of the surface under the node.
	///
	/// You can for example make all surfaces with a specific layer make the nodes get a specific tag.
	///
	/// [Open online documentation to see images]
	///
	/// See: grid-rules (view in online documentation for working links)
	/// </summary>
	[Pathfinding.Util.Preserve]
	public class RulePerLayerModifications : GridGraphRule {
		public PerLayerRule[] layerRules = new PerLayerRule[0];
		const int SetTagBit = 1 << 30;

		public struct PerLayerRule {
			/// <summary>Layer this rule applies to</summary>
			public int layer;
			/// <summary>The action to apply to matching nodes</summary>
			public RuleAction action;
			/// <summary>
			/// Tag for the RuleAction.SetTag action.
			/// Must be between 0 and <see cref="Pathfinding.GraphNode.MaxTagIndex"/>
			/// </summary>
			public int tag;
		}

		public enum RuleAction {
			SetTag,
			MakeUnwalkable,
		}

		public override void Register (GridGraphRules rules) {
			int[] layerToTag = new int[32];
			bool[] layerToUnwalkable = new bool[32];
			for (int i = 0; i < layerRules.Length; i++) {
				var rule = layerRules[i];
				if (rule.action == RuleAction.SetTag) {
					layerToTag[rule.layer] = SetTagBit | rule.tag;
				} else {
					layerToUnwalkable[rule.layer] = true;
				}
			}

			rules.AddMainThreadPass(Pass.BeforeConnections, context => {
				var raycastHits = context.data.heightHits;
				var nodeWalkable = context.data.nodeWalkable;
				var nodeTags = context.data.nodeTags;
				for (int i = 0; i < raycastHits.Length; i++) {
					var coll = raycastHits[i].collider;
					if (coll != null) {
						var layer = coll.gameObject.layer;
						if (layerToUnwalkable[layer]) nodeWalkable[i] = false;
						var tag = layerToTag[layer];
						if ((tag & SetTagBit) != 0) nodeTags[i] = tag & 0xFF;
					}
				}
			});
		}
	}
}
