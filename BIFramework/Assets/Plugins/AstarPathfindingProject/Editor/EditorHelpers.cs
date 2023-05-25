using UnityEngine;
using UnityEditor;

namespace Pathfinding.Util {
	/// <summary>Some editor gui helper methods</summary>
	public static class EditorGUILayoutHelper {
		/// <summary>
		/// Tag names and an additional 'Edit Tags...' entry.
		/// Used for SingleTagField
		/// </summary>
		static string[] tagNamesAndEditTagsButton;

		/// <summary>
		/// Last time tagNamesAndEditTagsButton was updated.
		/// Uses EditorApplication.timeSinceStartup
		/// </summary>
		static double timeLastUpdatedTagNames;

		public static int TagField (string label, int value, System.Action editCallback) {
			// Make sure the tagNamesAndEditTagsButton is relatively up to date
			if (tagNamesAndEditTagsButton == null || EditorApplication.timeSinceStartup - timeLastUpdatedTagNames > 1) {
				timeLastUpdatedTagNames = EditorApplication.timeSinceStartup;
				var tagNames = AstarPath.FindTagNames();
				tagNamesAndEditTagsButton = new string[tagNames.Length+1];
				tagNames.CopyTo(tagNamesAndEditTagsButton, 0);
				tagNamesAndEditTagsButton[tagNamesAndEditTagsButton.Length-1] = "Edit Tags...";
			}

			// Tags are between 0 and 31
			value = Mathf.Clamp(value, 0, 31);

			var newValue = EditorGUILayout.IntPopup(label, value, tagNamesAndEditTagsButton, new [] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, -1 });

			// Last element corresponds to the 'Edit Tags...' entry. Open the tag editor
			if (newValue == -1) {
				editCallback();
			} else {
				value = newValue;
			}

			return value;
		}
	}
}
