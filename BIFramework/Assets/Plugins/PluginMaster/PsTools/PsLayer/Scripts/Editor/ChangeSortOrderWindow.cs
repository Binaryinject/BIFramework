/*
Copyright (c) 2020 Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte, 2020.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
using UnityEditor;
using UnityEngine;

namespace PluginMaster
{
    public class ChangeSortOrderWindow : EditorWindow
    {
        private const string TITLE = "Change Sorting Order Recursively";
        private int _value = 0;
        private SpriteRenderer[] _renderers = null;

        [MenuItem("CONTEXT/SpriteRenderer/" + TITLE, false, 2100)]
        private static void ShowWindow(MenuCommand command)
        {
            var window = GetWindow<ChangeSortOrderWindow>(true, TITLE);
            window._renderers = (command.context as SpriteRenderer).GetComponentsInChildren<SpriteRenderer>(true);
        }

        [MenuItem("CONTEXT/PsGroup/" + TITLE, true, 2100)]
        private static bool ValidateWindowOnPsGroup(MenuCommand command)
        {
            var renderers = (command.context as PsGroup).GetComponentsInChildren<SpriteRenderer>(true);
            return renderers.Length > 0;
        }

        [MenuItem("CONTEXT/PsGroup/" + TITLE, false, 2100)]
        private static void ShowWindowOnPsGroup(MenuCommand command)
        {
            var window = GetWindow<ChangeSortOrderWindow>(true, TITLE);
            window._renderers = (command.context as PsGroup).GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void OnEnable() => maxSize = minSize = new Vector2(220, 44);

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 40;
            EditorGUIUtility.fieldWidth = 60;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope())
                {
                    _value = EditorGUILayout.IntField("Add:", _value);
                    if (GUILayout.Button("Apply"))
                    {
                        foreach (var renderer in _renderers)
                        {
                            Undo.RecordObject(renderer, TITLE);
                            renderer.sortingOrder += _value;
                        }
                        Close();
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}