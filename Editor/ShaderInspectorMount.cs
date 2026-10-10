using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DocMe
{
    /// <summary>
    /// Puts a UI Toolkit view right after the IMGUIContainer that hosts a ShaderGUI inside an InspectorElement,
    /// so the Material editor stays whoever owns it (Unity's or Poiyomi's). One view per MaterialEditor. When
    /// there is no such container (a window that calls OnInspectorGUI itself, or Unity renamed the internal
    /// lookup) or the user chose the Classic inspector, TryMount returns false and the IMGUI path draws.
    /// </summary>
    public static class ShaderInspectorMount
    {
        public const string ViewName = "docme-shader-inspector";

        sealed class GuiState
        {
            public MaterialEditor lastEditor;
            public bool mountQueued;
            public MaterialProperty[] pendingProperties;
            public IMGUIContainer failedContainer;
            public string search = "";

            /// <summary>The container a view is awaited for, the repaints it drew blank meanwhile, and what its last layout pass decided.</summary>
            public IMGUIContainer waitingContainer;
            public int blankRepaints;
            public bool gaveUp;
        }

        /// <summary>
        /// Repaints a host may draw blank while its view waits for the next editor tick. Past that something kept the
        /// deferred mount from running, and the classic inspector draws until the view arrives.
        /// </summary>
        public const int MaxBlankRepaints = 2;

        static readonly Func<IMGUIContainer> DefaultLookup = BuildContainerLookup();
        static readonly Dictionary<int, ShaderInspectorView> Views = new Dictionary<int, ShaderInspectorView>();
        static readonly ConditionalWeakTable<ShaderGUI, GuiState> States = new ConditionalWeakTable<ShaderGUI, GuiState>();
        static readonly List<Action> Pending = new List<Action>();
        static readonly List<(MaterialEditor editor, IMGUIContainer container)> AwaitingAnimationEnd = new List<(MaterialEditor, IMGUIContainer)>();
        static bool animationHooked;

        /// <summary>Test seam for the internal container lookup; return null to act as if Unity renamed it.</summary>
        public static Func<IMGUIContainer> ContainerLookupOverride;

        /// <summary>Test seam for MaterialEditor.isVisible, which reads the per type expanded state a test must not change.</summary>
        public static Func<MaterialEditor, bool> VisibilityOverride;

        /// <summary>Test seam: whether the material is shown under a renderer while animation mode is on.</summary>
        public static Func<MaterialEditor, bool> AnimationRenderersOverride;

        static readonly FieldInfo AnimatedRenderers = typeof(MaterialEditor).GetField("m_RenderersForAnimationMode", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly PropertyInfo InspectedEditor = typeof(InspectorElement).GetProperty("editor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static bool ContainerLookupAvailable => DefaultLookup != null;
        public static int LiveViewCount => Views.Count;
        public static int PendingCount => Pending.Count;
        public static int AwaitingAnimationEndCount => AwaitingAnimationEnd.Count;
        public static string LastStatus { get; private set; } = "";
        public static IEnumerable<ShaderInspectorView> LiveViews => Views.Values.ToArray();

        /// <summary>
        /// Called first thing in SimpleShaderGUI.OnGUI. True when the UI Toolkit view shows this material, or is
        /// about to on the next editor tick: OnGUI then draws nothing else. False when the IMGUI path must draw.
        /// </summary>
        public static bool TryMount(MaterialEditor editor, MaterialProperty[] properties, ShaderGUI gui)
        {
            try
            {
                return Track(editor, properties, gui);
            }
            catch (Exception e) when (!(e is ExitGUIException))
            {
                LastStatus = "error: " + e.Message;
                Debug.LogWarning("[DocMe Shader UI] the UI Toolkit inspector failed, drawing the classic one: " + e);
                return false;
            }
        }

        static bool Track(MaterialEditor editor, MaterialProperty[] properties, ShaderGUI gui)
        {
            if (editor == null || gui == null)
                return false;
            var state = States.GetOrCreateValue(gui);

            if (ShaderUiServices.Classic)
            {
                LastStatus = "classic inspector chosen";
                DropViewsOf(gui);
                return false;
            }

            var container = ContainerLookupOverride != null ? ContainerLookupOverride() : DefaultLookup?.Invoke();
            if (AnimatedUnderRenderer(editor))
            {
                LastStatus = "animation mode on a renderer, the classic inspector draws";
                DropViewsOf(gui);
                if (container != null)
                    AwaitAnimationEnd(editor, container);
                return false;
            }

            if (container == null)
            {
                LastStatus = "no hosting IMGUIContainer found";
                DropViewsOf(gui);
                return false;
            }
            var inspector = container.GetFirstAncestorOfType<InspectorElement>();
            if (inspector == null)
            {
                LastStatus = "the hosting IMGUIContainer is not inside an inspector";
                return false;
            }
            if (!Inspects(inspector, editor))
            {
                LastStatus = "the material is drawn inside another editor's inspector";
                DropViewsOf(gui);
                return false;
            }
            // Unity disables the whole inspector of a material that is not open for edit (an immutable package, a version
            // control lock); a view in there could not open its folds, while the classic inspector's folds still work.
            if (!container.enabledInHierarchy)
            {
                LastStatus = "the inspector is read only here, the classic inspector draws";
                DropViewsOf(gui);
                return false;
            }
            if (!TargetsLive(editor))
            {
                LastStatus = "a selected material is gone or uses another shader, the classic inspector draws";
                DropViewsOf(gui);
                return false;
            }

            if (state.lastEditor != null && state.lastEditor != editor)
            {
                var previous = state.lastEditor;
                Defer(() => UnmountEditor(previous));
            }
            state.lastEditor = editor;

            Views.TryGetValue(editor.GetInstanceID(), out var view);
            if (view == null || view.Container != container || view.Owner != gui)
            {
                if (state.failedContainer == container)
                {
                    LastStatus = "the view failed in this container, the classic inspector draws";
                    return false;
                }
                state.pendingProperties = properties;
                if (!state.mountQueued)
                {
                    state.mountQueued = true;
                    Defer(() => Mount(editor, container, gui, state));
                }
                else
                {
                    RegisterFlush();
                }
                if (WaitedTooLong(state, container))
                {
                    LastStatus = "the view did not mount in time, the classic inspector draws";
                    return false;
                }
                LastStatus = "mount queued";
                return true;
            }
            state.waitingContainer = null;
            state.blankRepaints = 0;
            state.gaveUp = false;

            view.SetHostProperties(properties);
            if (view.style.display == DisplayStyle.None || view.IsStale() || !view.MatchesShape(properties))
                view.QueueCheck();
            LastStatus = "mounted";
            return true;
        }

        /// <summary>
        /// Counts the repaints a host draws blank while its view waits, and gives up after MaxBlankRepaints. The decision
        /// changes only on a Layout event: IMGUI needs the Layout pass and the passes after it to draw the same controls.
        /// </summary>
        static bool WaitedTooLong(GuiState state, IMGUIContainer container)
        {
            if (state.waitingContainer != container)
            {
                state.waitingContainer = container;
                state.blankRepaints = 0;
                state.gaveUp = false;
            }
            var type = Event.current?.type ?? EventType.Layout;
            if (type == EventType.Repaint)
                state.blankRepaints++;
            else if (type == EventType.Layout)
                state.gaveUp = state.blankRepaints > MaxBlankRepaints;
            return state.gaveUp;
        }

        /// <summary>Every selected material exists and they share one shader, the only case in which Unity hands the selection to OnGUI.</summary>
        static bool TargetsLive(MaterialEditor editor)
        {
            var targets = editor.targets;
            if (targets.Length == 0 || !(targets[0] is Material first) || first == null)
                return false;
            var shader = first.shader;
            foreach (var target in targets)
                if (!(target is Material material) || material == null || material.shader != shader)
                    return false;
            return true;
        }

        /// <summary>
        /// A placed view that threw: it is removed, its container remembered so the classic inspector draws there from
        /// then on, and the failure logged once for that container.
        /// </summary>
        internal static void Fail(ShaderInspectorView view, Exception e)
        {
            var container = view.Container;
            bool first = true;
            if (view.Owner != null && container != null)
            {
                var state = States.GetOrCreateValue(view.Owner);
                first = state.failedContainer != container;
                state.failedContainer = container;
            }
            if (first)
                Debug.LogWarning("[DocMe Shader UI] the UI Toolkit inspector failed, drawing the classic one: " + e);
            Unmount(view);
            if (container != null)
            {
                container.MarkDirtyLayout();
                container.MarkDirtyRepaint();
            }
            if (view.Editor != null)
                view.Editor.Repaint();
        }

        /// <summary>
        /// In animation mode Unity draws a material under a renderer from the renderer's animated values, tints keyed
        /// fields and offers Add Key on right click; only its own IMGUI inspector does that, so it draws instead.
        /// During OnGUI the host keeps the renderers it animates in a private field.
        /// </summary>
        static bool AnimatedUnderRenderer(MaterialEditor editor)
        {
            if (!AnimationMode.InAnimationMode())
                return false;
            if (AnimationRenderersOverride != null)
                return AnimationRenderersOverride(editor);
            if (AnimatedRenderers != null)
                return AnimatedRenderers.GetValue(editor) is Renderer[] renderers && renderers.Length > 0;
            return MaterialEditor.PrepareMaterialPropertiesForAnimationMode(Array.Empty<MaterialProperty>(), true) != null;
        }

        /// <summary>
        /// Ending an Animation window preview does not repaint the Inspector, so a host drawing the classic inspector
        /// for animation mode is asked to draw again once the mode ends, and the view comes back at once.
        /// </summary>
        static void AwaitAnimationEnd(MaterialEditor editor, IMGUIContainer container)
        {
            foreach (var waiting in AwaitingAnimationEnd)
                if (waiting.editor == editor && waiting.container == container)
                    return;
            AwaitingAnimationEnd.Add((editor, container));
            if (animationHooked)
                return;
            animationHooked = true;
            EditorApplication.update += CheckAnimationEnd;
        }

        /// <summary>Runs on the editor update while a host waits; unhooks as soon as nothing waits any more.</summary>
        internal static void CheckAnimationEnd()
        {
            bool animating = AnimationMode.InAnimationMode();
            for (int i = AwaitingAnimationEnd.Count - 1; i >= 0; i--)
            {
                var (editor, container) = AwaitingAnimationEnd[i];
                bool gone = editor == null || container == null || container.panel == null;
                if (!gone && animating)
                    continue;
                AwaitingAnimationEnd.RemoveAt(i);
                if (gone)
                    continue;
                container.MarkDirtyLayout();
                container.MarkDirtyRepaint();
                editor.Repaint();
            }
            if (AwaitingAnimationEnd.Count > 0 || !animationHooked)
                return;
            animationHooked = false;
            EditorApplication.update -= CheckAnimationEnd;
        }

        /// <summary>False when the container belongs to another editor that draws this material inline (a component inspector calling OnInspectorGUI).</summary>
        static bool Inspects(InspectorElement inspector, MaterialEditor editor)
        {
            if (InspectedEditor == null)
                return true;
            var owner = InspectedEditor.GetValue(inspector) as UnityEditor.Editor;
            return owner == null || ReferenceEquals(owner, editor);
        }

        /// <summary>A reimported shader may carry new attributes: its views rebuild on the next host pass.</summary>
        internal static void ShadersChanged(ICollection<string> paths)
        {
            foreach (var view in LiveViews)
            {
                if (!paths.Contains(view.ShaderPath))
                    continue;
                var captured = view;
                Defer(() =>
                {
                    Unmount(captured);
                    captured.Container?.MarkDirtyLayout();
                    if (captured.Editor != null)
                        captured.Editor.Repaint();
                });
            }
        }

        /// <summary>Called from SimpleShaderGUI.OnClosed: Unity is done with this ShaderGUI (the shader menu changed the shader).</summary>
        public static void OnClosed(ShaderGUI gui) => DropViewsOf(gui);

        internal static string SearchOf(ShaderGUI gui) => gui != null && States.TryGetValue(gui, out var state) ? state.search : "";

        internal static void RememberSearch(ShaderGUI gui, string search)
        {
            if (gui != null)
                States.GetOrCreateValue(gui).search = search ?? "";
        }

        static void DropViewsOf(ShaderGUI gui)
        {
            foreach (var view in LiveViews)
                if (view.Owner == gui)
                    Defer(() => Unmount(view));
        }

        static void Mount(MaterialEditor editor, IMGUIContainer container, ShaderGUI gui, GuiState state)
        {
            state.mountQueued = false;
            if (editor == null || editor.target == null || container.panel == null || ShaderUiServices.Classic)
                return;
            var parent = container.hierarchy.parent;
            if (parent == null)
                return;

            if (Views.TryGetValue(editor.GetInstanceID(), out var existing))
            {
                if (existing.Container == container && existing.Owner == gui && existing.hierarchy.parent == parent)
                    return;
                Unmount(existing);
            }

            ShaderInspectorView view;
            try
            {
                view = new ShaderInspectorView(editor, gui, container, state.pendingProperties) { name = ViewName };
            }
            catch (Exception e)
            {
                state.failedContainer = container;
                Debug.LogWarning("[DocMe Shader UI] the UI Toolkit inspector could not be built, drawing the classic one: " + e);
                container.MarkDirtyLayout();
                editor.Repaint();
                return;
            }
            state.pendingProperties = null;

            parent.hierarchy.Insert(parent.hierarchy.IndexOf(container) + 1, view);
            Views[editor.GetInstanceID()] = view;
            container.RegisterCallback<DetachFromPanelEvent>(view.OnContainerDetached);
            view.Check();
        }

        static void UnmountEditor(MaterialEditor editor)
        {
            foreach (var pair in Views.ToArray())
                if (pair.Value.Editor == editor || pair.Value.Editor == null)
                    Unmount(pair.Value);
        }

        public static void Unmount(ShaderInspectorView view)
        {
            if (view == null)
                return;
            foreach (var pair in Views.Where(p => p.Value == view).ToArray())
                Views.Remove(pair.Key);
            view.Container?.UnregisterCallback<DetachFromPanelEvent>(view.OnContainerDetached);
            view.ReleaseHooks();
            view.RemoveFromHierarchy();
        }

        public static void CheckAll()
        {
            foreach (var view in LiveViews)
                view.Check();
        }

        public static bool HostShowsBody(MaterialEditor editor)
        {
            if (editor == null)
                return false;
            return VisibilityOverride != null ? VisibilityOverride(editor) : editor.isVisible;
        }

        /// <summary>
        /// OnGUI runs inside Yoga's measure pass and inside the render callback, where a change to the tree is
        /// unsafe, so every structural change waits for the next editor tick. Tests call Flush directly.
        /// </summary>
        public static void Defer(Action action)
        {
            Pending.Add(action);
            RegisterFlush();
        }

        /// <summary>
        /// Registers Flush for the next editor tick, exactly once. Unity drops the rest of a delayCall batch when an
        /// earlier callback in it throws, so a host pass that still waits renews this rather than trust a flag.
        /// </summary>
        static void RegisterFlush()
        {
            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        public static void Flush()
        {
            EditorApplication.delayCall -= Flush;
            var batch = Pending.ToArray();
            Pending.Clear();
            foreach (var action in batch)
            {
                try { action(); }
                catch (Exception e) { Debug.LogWarning("[DocMe Shader UI] a deferred step failed: " + e); }
            }
        }

        static Func<IMGUIContainer> BuildContainerLookup()
        {
            try
            {
                var type = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsUtility");
                var method = type?.GetMethod("GetCurrentIMGUIContainer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (method == null || method.ReturnType != typeof(IMGUIContainer) || method.GetParameters().Length != 0)
                    return null;
                return (Func<IMGUIContainer>)Delegate.CreateDelegate(typeof(Func<IMGUIContainer>), method);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
