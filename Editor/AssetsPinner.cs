﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Tools.AssetsPinner
{
    public class AssetsPinner : EditorWindow
    {
        #region Local Storage
        private static readonly string s_ProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
        private static readonly string s_UserSettingsPath = Path.Combine(s_ProjectPath, "UserSettings");

        internal const string BaseName = "AssetsPinner.json";
        internal static readonly string LocalFilePathTemplate = Path.GetFullPath(Path.Combine(s_UserSettingsPath, BaseName));

        internal static T GetLocal<T>(string path) where T : new()
        {
            if (!File.Exists(path))
            {
                return default;
            }
            string jsonString = File.ReadAllText(path, Encoding.UTF8);
            try
            {
                return JsonUtility.FromJson<T>(jsonString);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return default;
            }
        }

        internal static void SetLocal<T>(string path, T value)
        {
            if (!File.Exists(path))
            {
                FileStream fs = File.Create(path);
                fs.Close();
            }
            string jsonString = JsonUtility.ToJson(value, true);
            File.WriteAllText(path, jsonString, Encoding.UTF8);
        }

        #endregion

        private const string StrShowTab = "Tabs";
        private const string StrShowOptions = "Options";
        private const string StrReset = "Reset";
        private const string StrDrag = "Drag and drop assets here";
        private const string StrOpen = "Ping";
        private const string StrRemove = "-";
        private bool isDrag;


        public List<CacheTabInfo> tabs = new List<CacheTabInfo>() { new CacheTabInfo { id = 0, tabName = "Common" } };
        public List<CacheObjectInfo> objects = new List<CacheObjectInfo>();
        public List<CacheObjectInfo> filterList = new List<CacheObjectInfo>();

        private Vector2 _objectsScrollPosition;
        private Vector2 _tabsScrollPosition;
        private EditorCacheStyle _s;
        private int _oldTabIndex = -1;
        private int _tabIndex;
        private bool _isEditMode;
        private bool _isShowTabs;
        private bool _showOptions;
        private ReorderableList _objectsReorderableList;
        private ReorderableList _tabsReorderableList;

        private float _currentTabViewWidth;
        private bool _isResize;
        private Rect _cursorChangeRect;
        private GUILayoutOption expandWidthFalse = GUILayout.ExpandWidth(false);

        private void OnEnable()
        {
            InitReOrderTabList();

            _currentTabViewWidth = 110;
            _cursorChangeRect = new Rect(_currentTabViewWidth, 0, 5, position.size.y);

            EditorApplication.update += Update;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Update;
        }

        [MenuItem("Tools/Assets Pinner")]
        public static void Init()
        {
            var w = GetWindow<AssetsPinner>();
            w.titleContent.text = "Assets Pinner";
            w.Read();
            w.Show();
        }

        private void Update()
        {

        }

        private void InitReOrderTabList()
        {
            _tabsReorderableList = new ReorderableList(tabs, typeof(CacheTabInfo), true, false, true, true);
            _tabsReorderableList.drawElementCallback += DrawTabElementCallback;
            _tabsReorderableList.onChangedCallback += _ => { Save(); };
            _tabsReorderableList.onAddCallback += _ => { AddNewTab(); };
        }

        private void FilterWhenTabChanged()
        {
            if (_oldTabIndex != _tabIndex)
            {
                if (tabs.Count > 0)
                {
                    _oldTabIndex = _tabIndex;
                    if (tabs[_tabIndex].list == null) tabs[_tabIndex].list = new List<CacheObjectInfo>();
                    objects = tabs[_tabIndex].list;
                    Filter();
                }
                else
                    tabs = new List<CacheTabInfo> { new CacheTabInfo() { id = 0, tabName = "Common" } };

                EditorGUI.FocusTextInControl("");
                UpdatePreviewSize();
            }
        }

        private void UpdatePreviewSize()
        {

        }

        private void OnGUI()
        {
            if (_s == null)
            {
                _s = new EditorCacheStyle();
                UpdatePreviewSize();
            }

            FilterWhenTabChanged();
            DisplayTopBar();
            GUILayout.BeginHorizontal();
            if (_isShowTabs)
            {
                DisplayTabs();
            }

            DisplayObjectGroup();

            GUILayout.EndHorizontal();
        }

        private void ShowOptions()
        {
            bool ShowPath = false;
            bool ShowNumber = false;

            ShowPath = GUILayout.Toggle(ShowPath, "ShowPath", _s.expandWidthFalse);
            ShowNumber = GUILayout.Toggle(ShowNumber, "ShowNumber", _s.expandWidthFalse);

            EditorGUIUtility.labelWidth = 60;
            var iconSize = EditorGUILayout.Slider("Icon Size", 20, 20, 100);

            UpdatePreviewSize();
            Save();
        }

        private void DisplayObjects()
        {
            GUILayout.BeginVertical();
            if (_showOptions)
            {
                ShowOptions();
            }

            _objectsScrollPosition = GUILayout.BeginScrollView(_objectsScrollPosition);
            if (_isEditMode) DisplayReorderList();
            else
            {
                DisplayListObjects();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DisplayObjectGroup()
        {
            if (objects == null) return;
            DisplayObjects();
            UpdateDragAndDrop();
        }

        private void DisplayTopBar()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(StrShowTab, _isShowTabs ? _s.buttonStyle : EditorStyles.toolbarButton, _s.expandWidthFalse))
            {
                _isShowTabs = !_isShowTabs;
            }

            if (GUILayout.Button(StrShowOptions, _showOptions ? _s.buttonStyle : EditorStyles.toolbarButton, _s.expandWidthFalse))
            {
                _showOptions = !_showOptions;
                _isEditMode = _showOptions;
            }

            if (GUILayout.Button("Delete All", EditorStyles.toolbarButton, _s.expandWidthFalse))
            {
                objects.Clear();
                filterList.Clear();
                Focus();
                Save();
            }

            GUILayout.EndHorizontal();
        }

        private void DisplayReorderList()
        {
            GUILayout.BeginVertical();
            _objectsReorderableList.DoLayoutList();
            GUILayout.EndVertical();
        }

        private void DisplayListObjects()
        {
            GUILayout.BeginVertical();
            if (filterList.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Label(StrDrag, _s.textEmpty);
                EditorGUI.EndDisabledGroup();
            }

            int count = 1;
            bool isHoleControl = Event.current.control;
            bool isHoleAlt = Event.current.alt;
            bool isMouseDown = Event.current.type == EventType.MouseDown;
            foreach (var o in filterList)
            {
                bool ignore = false;
                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                {
                    if (o.previewTexture == null)
                    {
                        o.previewTexture = AssetPreview.GetAssetPreview(o.obj);
                        if (o.previewTexture == null && !o.isPrefab)
                        {
                            o.previewTexture = AssetPreview.GetMiniThumbnail(o.obj);
                        }
                    }

                    GUILayout.Label(o.previewTexture, _s.previewTexture);
                    GUILayout.Label(o.GetDisplayName(), GUILayout.MinWidth(30));

                    if (GUILayout.Button("O", _s.expandWidthFalse))
                    {
                        if (o.obj == null)
                        {
                            o.Ping();
                        }
                        else if (o.obj is DefaultAsset)
                        {
                            AssetDatabase.OpenAsset(o.obj);
                        }
                        else if (!string.IsNullOrEmpty(o.prefabPath))
                        {
                            AssetDatabase.OpenAsset(o.obj);
                        }
                        else
                        {
                            AssetDatabase.OpenAsset(o.obj);
                        }
                    }

                    if (o.location == CacheObjectLocation.Assets)
                    {
                        if (GUILayout.Button("P", _s.expandWidthFalse))
                        {
                            ignore = true;
                            o.Ping();
                        }
                    }

                    if (GUILayout.Button("-", _s.expandWidthFalse))
                    {
                        ignore = true;
                        UpdateRemove(o);
                        GUILayout.EndHorizontal();
                        break;
                    }
                }
                GUILayout.EndHorizontal();

                var itemRect = GUILayoutUtility.GetLastRect();
                if (!ignore && Event.current.type == EventType.MouseDrag &&
                itemRect.Contains(Event.current.mousePosition))
                {
                    isDrag = true;
                    ignore = true;
                    GUIUtility.hotControl = 0;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new[] { o.obj };
                    DragAndDrop.SetGenericData("DRAG_ID", o.obj);
                    DragAndDrop.StartDrag("A");
                }

                count++;
            }

            GUILayout.EndVertical();
        }

        public void UpdateRemove(CacheObjectInfo o)
        {
            objects.Remove(o);
            filterList.Remove(o);
            Save();
            Focus();
        }

        private void ResizeScrollView()
        {
            EditorGUIUtility.AddCursorRect(_cursorChangeRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && _cursorChangeRect.Contains(Event.current.mousePosition))
            {
                _isResize = true;
            }

            if (_isResize)
            {
                _currentTabViewWidth = Event.current.mousePosition.x;
                _currentTabViewWidth = Mathf.Clamp(_currentTabViewWidth, 100, 200);
                _cursorChangeRect.Set(_currentTabViewWidth, _cursorChangeRect.y, _cursorChangeRect.width,
                    _cursorChangeRect.height);
                Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
                _isResize = false;
        }

        private void DisplayTabs()
        {
            GUILayout.BeginVertical(_s.ProjectBrowserIconAreaBg, GUILayout.Width(_currentTabViewWidth));
            _tabsScrollPosition = GUILayout.BeginScrollView(_tabsScrollPosition);
            if (!_isEditMode)
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    var buttonStyle = i == _tabIndex ? _s.SelectionRect : _s.RectangleToolSelection;

                    if (_isEditMode && i == _tabIndex)
                    {
                        GUILayout.BeginVertical();
                        tabs[i].tabName = EditorGUILayout.TextField(tabs[i].tabName, _s.expandWidth200);
                        GUILayout.EndVertical();
                    }
                    else
                    {
                        if (GUILayout.Button(tabs[i].tabName, buttonStyle, GUILayout.Width(_currentTabViewWidth - 5)))
                        {
                            _tabIndex = i;
                        }
                    }
                }
            }
            else
            {
                _tabsReorderableList.DoLayoutList();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            ResizeScrollView();
        }

        private void AddNewTab()
        {
            if (tabs.Count == 0)
            {
                tabs.Add(new CacheTabInfo { id = 0, tabName = "New tab", list = new List<CacheObjectInfo>() });
            }
            else
            {
                tabs.Add(new CacheTabInfo
                { id = tabs.Max(s => s.id) + 1, tabName = "New tab", list = new List<CacheObjectInfo>() });
            }

            Save();
        }

        private void Filter()
        {
            filterList = objects;

            _objectsReorderableList =
                new ReorderableList(filterList, typeof(CacheObjectInfo), true, false, false, false);
            _objectsReorderableList.drawElementCallback += DrawObjectElementCallback;
            _objectsReorderableList.onChangedCallback += _ => { Save(); };
        }

        private void DrawObjectElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var data = filterList[index];
            var avatarRect = new Rect(rect.x, rect.y, rect.height, rect.height);
            GUI.Label(avatarRect, data.previewTexture);
            var nameRect = new Rect(rect.x + rect.height + 3, rect.y, rect.width - rect.height - 3, rect.height);
            GUI.Label(nameRect, data.GetDisplayName());
        }

        private void DrawTabElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var data = tabs[index];
            var nameRect = new Rect(rect.x, rect.y, rect.width, rect.height);
            data.tabName = GUI.TextField(nameRect, data.tabName);
        }

        void UpdateDragAndDrop()
        {
            if (Event.current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (isDrag)
                {
                    isDrag = false;
                    return;
                }

                if (DragAndDrop.paths.Length == 0 && DragAndDrop.objectReferences.Length > 0)
                {
                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        var currentPrefab = PrefabStageUtility.GetCurrentPrefabStage();
                        string assetPath = obj.name;
                        var transform = (obj as GameObject).transform;
                        GetScenePath(transform.transform, ref assetPath);
                        var item = new CacheObjectInfo
                        {
                            Name = obj.name,
                            path = assetPath,
                            parents = GetPrefabParents(new List<string>(), transform)
                        };
                        item.SetDisplayName();

                        if (currentPrefab != null)
                        {
                            item.prefabPath = currentPrefab.assetPath;
                            item.location = CacheObjectLocation.Prefab;
                        }
                        else
                        {
                            item.location = CacheObjectLocation.Scene;
                        }

                        if (objects.Find(s => s.obj == item.obj) == null)
                        {
                            objects.Add(item);
                            Save();
                        }
                        else if (objects.Find(s => s.path == item.path) == null)
                        {
                            objects.Add(item);
                            Save();
                        }
                    }

                    Filter();
                }
                else if (DragAndDrop.paths.Length > 0 && DragAndDrop.objectReferences.Length == 0)
                {
                    foreach (string path in DragAndDrop.paths)
                    {
                        Debug.Log("- " + path);
                    }
                }
                else if (DragAndDrop.paths.Length == DragAndDrop.objectReferences.Length)
                {
                    for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                    {
                        Object obj = DragAndDrop.objectReferences[i];
                        if (objects.Find(s => s.obj == obj) == null)
                        {
                            var item = new CacheObjectInfo
                            {
                                Name = obj.name,
                                location = CacheObjectLocation.Assets,
                                obj = obj,
                                path = AssetDatabase.GetAssetPath(obj),
                            };
                            item.SetDisplayName();
                            objects.Add(item);
                            Save();
                        }
                    }

                    Filter();
                }
            }
        }

        private List<string> GetPrefabParents(List<string> list, Transform target)
        {
            if (target.parent != null)
            {
                list.Insert(0, target.parent.name);
                GetPrefabParents(list, target.parent);
            }

            return list;
        }

        private void GetScenePath(Transform obj, ref string path)
        {
            var parent = obj.parent;
            if (parent != null)
            {
                path = parent.name + "/" + path;
                GetScenePath(parent, ref path);
            }
        }

        [Serializable]
        public class CacheObjectInfo
        {
            public string guid = string.Empty;
            public Object obj;
            public string path;
            public string prefabPath;
            public bool isPrefab;
            public string Name;
            public CacheObjectLocation location;
            public Texture2D previewTexture;
            public string displayName;
            public List<string> parents = new List<string>();

            public void SetDisplayName()
            {
                isPrefab = path.EndsWith(".prefab");
                displayName = Name;
                // displayName = $"{GetPrefix()} {Name}";
            }

            public string GetDisplayName()
            {
                return displayName;
            }

            public string GetPrefix()
            {
                if (location == CacheObjectLocation.Assets)
                    return "A:";
                if (location == CacheObjectLocation.Scene)
                    return "S:";
                return "P:";
            }

            public void OpenPrefab()
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath(prefabPath, typeof(GameObject)));
                var rootGameObjects = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot;

                var arrayPath = path.Split('/').ToList();
                arrayPath.RemoveAt(0);
                arrayPath.RemoveAt(0);
                var newPath = string.Join("/", arrayPath);
                var obj = rootGameObjects.transform.Find(newPath);
                if (obj != null)
                {
                    SceneView.lastActiveSceneView.LookAt(obj.gameObject.transform.position);
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
            }

            public void PingLocation(int locationIndex)
            {
                if (location == CacheObjectLocation.Prefab)
                {
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath(prefabPath, typeof(GameObject)));
                    var rootGameObjects = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot;

                    var arrayPath = path.Split('/').ToList();
                    arrayPath.RemoveAt(0);
                    arrayPath.RemoveAt(0);

                    var newPath = string.Join("/", arrayPath);
                    var obj = rootGameObjects.transform.Find(newPath);
                    if (obj != null)
                    {
                        SceneView.lastActiveSceneView.LookAt(obj.gameObject.transform.position);
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
            }

            public void Ping()
            {
                if (location == CacheObjectLocation.Assets)
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
                else if (location == CacheObjectLocation.Prefab)
                {
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath(prefabPath, typeof(GameObject)));
                    var rootGameObjects = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot;

                    var arrayPath = path.Split('/').ToList();
                    arrayPath.RemoveAt(0);
                    arrayPath.RemoveAt(0);
                    var newPath = string.Join("/", arrayPath);
                    var obj = rootGameObjects.transform.Find(newPath);
                    if (obj != null)
                    {
                        SceneView.lastActiveSceneView.LookAt(obj.gameObject.transform.position);
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
                else if (location == CacheObjectLocation.Scene)
                {
                    if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                    {
                        PrefabUtility.UnloadPrefabContents(
                            PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot);
                    }

                    var arrayPath = path.Split('/').ToList();
                    arrayPath.RemoveAt(0);
                    var newPath = string.Join("/", arrayPath);

                    if (arrayPath.Count == 0)
                        newPath = path;

                    Transform obj = null;
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        bool check = false;
                        var scene = SceneManager.GetSceneAt(i);
                        var rootGameObjects = scene.GetRootGameObjects().ToList();
                        foreach (var gameObject in rootGameObjects)
                        {
                            obj = gameObject.transform.Find(newPath);
                            if (obj != null)
                            {
                                check = true;
                                break;
                            }
                        }

                        if (check)
                            break;

                        var tempObj = rootGameObjects.Find(s => s.name == newPath);
                        if (tempObj != null)
                        {
                            obj = tempObj.transform;
                            break;
                        }
                    }

                    if (obj != null)
                    {
                        SceneView.lastActiveSceneView.LookAt(obj.gameObject.transform.position);
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
            }
        }

        private void Save()
        {
            SetLocal(LocalFilePathTemplate, new EditorCacheSave { tabs = tabs });
        }

        private void Read()
        {
            EditorCacheSave editorCacheSave;
            editorCacheSave = GetLocal<EditorCacheSave>(LocalFilePathTemplate);
            if (editorCacheSave == null)
            {
                editorCacheSave = new EditorCacheSave
                { tabs = new List<CacheTabInfo> { new CacheTabInfo() { id = 0, tabName = "Common" } } };
            }

            tabs.Clear();
            tabs.AddRange(editorCacheSave.tabs);
            foreach (var tabInfo in tabs)
            {
                foreach (var item in tabInfo.list)
                {
                    if (string.IsNullOrEmpty(item.displayName))
                    {
                        item.SetDisplayName();
                    }
                }
            }

            InitReOrderTabList();
        }

        [Serializable]
        public class EditorCacheSave
        {
            public List<CacheTabInfo> tabs = new List<CacheTabInfo>();
        }

        public enum CacheObjectLocation
        {
            Assets,
            Scene,
            Prefab,
        }

        [Serializable]
        public class CacheTabInfo
        {
            public int id;
            public string tabName;
            public List<CacheObjectInfo> list = new List<CacheObjectInfo>();
            public CacheObjectInfo selected;
            public Editor editor;
        }
    }

    public class EditorCacheStyle
    {
        public readonly GUILayoutOption expandWidthFalse = GUILayout.ExpandWidth(false);
        public readonly GUILayoutOption expandWidth200 = GUILayout.Width(100);
        public readonly GUIStyle previewTexture;
        public readonly GUIStyle buttonStyle;
        public readonly GUIStyle textEmpty;
        public readonly GUIStyle toolBar;

        public readonly GUIStyle buttonLeftSelected;
        public readonly GUIStyle buttonMidSelected;
        public readonly GUIStyle buttonRightSelected;

        public readonly GUIStyle toolbarSeachTextField;
        public readonly GUIStyle RectangleToolSelection;
        public readonly GUIStyle ProjectBrowserIconAreaBg;
        public readonly GUIStyle SelectionRect;
        public readonly GUIStyle parent;
        public readonly GUIStyle text1;

        public EditorCacheStyle()
        {
            toolbarSeachTextField = GUI.skin.GetStyle(GUI.skin.textField.name);
            ProjectBrowserIconAreaBg = GUI.skin.GetStyle("ProjectBrowserIconAreaBg");
            RectangleToolSelection = GUI.skin.GetStyle("RectangleToolSelection");
            SelectionRect = GUI.skin.GetStyle("SelectionRect");
            previewTexture = new GUIStyle(GUI.skin.box)
            { fixedWidth = 20, fixedHeight = 20, alignment = TextAnchor.MiddleCenter };
            buttonLeftSelected = new GUIStyle(EditorStyles.miniButtonLeft);
            buttonMidSelected = new GUIStyle(EditorStyles.miniButtonMid);
            buttonRightSelected = new GUIStyle(EditorStyles.miniButtonRight);
            buttonLeftSelected.normal.textColor = buttonMidSelected.normal.textColor =
                buttonRightSelected.normal.textColor = Color.yellow;
            buttonLeftSelected.onHover.textColor = buttonMidSelected.onHover.textColor =
                buttonRightSelected.onHover.textColor = Color.yellow;

            buttonLeftSelected.focused.textColor = buttonMidSelected.focused.textColor =
                buttonRightSelected.focused.textColor = Color.yellow;

            buttonLeftSelected.fontStyle = buttonMidSelected.fontStyle =
                buttonRightSelected.fontStyle = FontStyle.Bold;

            ColorUtility.TryParseHtmlString("#363636", out var bgColor);
            buttonStyle = new GUIStyle("Button");
            buttonStyle.alignment = TextAnchor.MiddleLeft;
            textEmpty = new GUIStyle(EditorStyles.label);
            textEmpty.alignment = TextAnchor.MiddleCenter;
            toolBar = new GUIStyle(EditorStyles.toolbar);
            toolBar.fixedHeight = 60;

            parent = new GUIStyle(GUI.skin.box)
            {
                normal =
                {
                    textColor = new Color(.6f, .6f, .6f, .8f)
                },
                fontSize = 10,
                padding = new RectOffset(5, 5, 5, 5)
            };
            text1 = new GUIStyle()
            {
                normal =
                {
                    textColor = new Color(.6f, .6f, .6f, .8f)
                },
                fontSize = 10,
                padding = new RectOffset(5, 5, 5, 5)
            };
        }
    }

    public static class EditorCacheHelper
    {
        public static void ShowFolderContents(int folderInstanceID)
        {
            Assembly editorAssembly = typeof(Editor).Assembly;
            Type projectBrowserType = editorAssembly.GetType("UnityEditor.ProjectBrowser");
            MethodInfo showFolderContents = projectBrowserType.GetMethod(
                "ShowFolderContents", BindingFlags.Instance | BindingFlags.NonPublic);
            Object[] projectBrowserInstances = Resources.FindObjectsOfTypeAll(projectBrowserType);
            if (projectBrowserInstances.Length > 0)
            {
                for (int i = 0; i < projectBrowserInstances.Length; i++)
                    ShowFolderContentsInternal(projectBrowserInstances[i], showFolderContents, folderInstanceID);
            }
            else
            {
                EditorWindow projectBrowser = OpenNewProjectBrowser(projectBrowserType);
                ShowFolderContentsInternal(projectBrowser, showFolderContents, folderInstanceID);
            }
        }

        private static void ShowFolderContentsInternal(Object projectBrowser, MethodInfo showFolderContents,
            int folderInstanceID)
        {
            SerializedObject serializedObject = new SerializedObject(projectBrowser);
            bool inTwoColumnMode = serializedObject.FindProperty("m_ViewMode").enumValueIndex == 1;

            if (!inTwoColumnMode)
            {
                MethodInfo setTwoColumns = projectBrowser.GetType().GetMethod(
                    "SetTwoColumns", BindingFlags.Instance | BindingFlags.NonPublic);
                setTwoColumns.Invoke(projectBrowser, null);
            }

            bool revealAndFrameInFolderTree = true;
            showFolderContents.Invoke(projectBrowser, new object[] { folderInstanceID, revealAndFrameInFolderTree });
        }

        private static EditorWindow OpenNewProjectBrowser(Type projectBrowserType)
        {
            EditorWindow projectBrowser = EditorWindow.GetWindow(projectBrowserType);
            projectBrowser.Show();
            MethodInfo init = projectBrowserType.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public);
            init.Invoke(projectBrowser, null);
            return projectBrowser;
        }
    }
}