using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ProtoSprite.Editor
{
    public class PromptWindow : EditorWindow
    {
        public string m_URL = "https://u3d.as/37u4#reviews";

        public static void Open()
        {
            var window = EditorWindow.GetWindow(typeof(PromptWindow));

            window.position = new Rect(Screen.currentResolution.width / 2 - 200, Screen.currentResolution.height / 2 - 100, 400, 200);
        }

		void OnGUI()
        {
            titleContent = new GUIContent("Support ProtoSprite");

            //GUILayout.Label("Help improve ProtoSprite!", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            //scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            //EditorGUILayout.BeginHorizontal();

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true
            };

            GUILayout.Space(20);

            GUILayout.Label("Hey, if you're enjoying ProtoSprite it would help so much if you could leave a rating on the Unity Asset Store. It really does help a lot with supporting the asset and only takes a couple of clicks. Thanks for reading and I wish you all the best with your game development!", style);

            //EditorGUILayout.LabelField(longText, GUILayout.MaxWidth(position.width));
            //EditorGUILayout.EndHorizontal();

            GUILayout.Space(20);

            //EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("I'll do it! (open in browser)", GUILayout.Height(40), GUILayout.Width(200)))
            {
                Application.OpenURL(m_URL);
                ProtoSpriteWindow.GetInstance().PromptState = 2;
                Close();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();


            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("No thanks!", GUILayout.Height(25), GUILayout.Width(200)))
            {
                ProtoSpriteWindow.GetInstance().PromptState = 2;
                Close();
            }
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }
    }
}
