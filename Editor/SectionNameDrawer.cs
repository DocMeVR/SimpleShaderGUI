using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe
{
    [CustomPropertyDrawer(typeof(SectionNameAttribute))]
    public class SectionNameDrawer : PropertyDrawer
    {
        // Set by SimpleShaderConfigEditor right before it draws the config, from the
        // [Foldable(...)] titles of the shader assigned in the Inspector.
        public static List<string> Sections;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            bool hasSections = Sections != null && Sections.Count > 0;

            Rect fieldRect = position;
            if (hasSections)
                fieldRect.width -= 24f;
            EditorGUI.PropertyField(fieldRect, property, label);

            if (!hasSections)
                return;

            Rect btnRect = new Rect(position.xMax - 22f, position.y, 22f, position.height);
            if (GUI.Button(btnRect, "▾", EditorStyles.miniButton))
            {
                string path = property.propertyPath;
                SerializedObject so = property.serializedObject;
                string current = property.stringValue;

                var menu = new GenericMenu();
                foreach (var s in Sections)
                {
                    string captured = s;
                    menu.AddItem(new GUIContent(s), current == s, () =>
                    {
                        var p = so.FindProperty(path);
                        if (p != null)
                        {
                            p.stringValue = captured;
                            so.ApplyModifiedProperties();
                        }
                    });
                }
                menu.DropDown(btnRect);
            }
        }
    }
}
