using UnityEngine;
using UnityEditor.ShortcutManagement;
using System;

namespace ProtoSprite.Editor
{
	public static class InternalEngineBridge
	{
#if UNITY_2023_3_OR_NEWER
		public class ShortcutContext : IShortcutContext
#else
		public class ShortcutContext : IShortcutToolContext
#endif
		{
			static Type[] s_PrioritizedContextTypes = new Type[] {
				typeof(UnityEditor.CameraFlyModeContext)
			};

			public bool active
			{
				get
				{
					//return true;// GUIUtility.hotControl == 0;
					foreach (var t in s_PrioritizedContextTypes)
					{
						if (ShortcutIntegration.instance.contextManager.HasPriorityContextOfType(t))
							return false;
					}

					return true;
				}
			}
		}

		public static void RegisterShortcutContext(ShortcutContext context)
		{
			ShortcutIntegration.instance.contextManager.RegisterToolContext(context);
		}

		public static void UnregisterShortcutContext(ShortcutContext context)
		{
			ShortcutIntegration.instance.contextManager.DeregisterToolContext(context);
		}
	}
}