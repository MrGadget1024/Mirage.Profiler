using System;
using System.Collections.Generic;
using System.IO;
using Mirage.NetworkProfiler.ModuleGUI.UITable;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mirage.NetworkProfiler.ModuleGUI.Messages
{
    internal sealed class MessageViewController : ProfilerModuleViewController
    {
        private readonly string _saveDataPath;
        private readonly CounterNames _names;
        private readonly ICountRecorderProvider _counterProvider;
        private readonly Columns _columns = new Columns();
        private Label _countLabel;
        private Label _bytesLabel;
        private Label _perSecondLabel;
        private VisualElement _toggleBox;
        private Toggle _debugToggle;
        private Toggle _groupMsgToggle;
        private MessageView _messageView;
        private readonly SavedData _savedData;

        public MessageViewController(ProfilerWindow profilerWindow, CounterNames names, string name, ICountRecorderProvider counterProvider) : base(profilerWindow)
        {
            _names = names;
            _counterProvider = counterProvider;

            var userSettingsFolder = Path.GetFullPath("UserSettings");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            _saveDataPath = Path.Join(userSettingsFolder, "Mirage.Profiler", $"{name}.json");
            Debug.Log($"Load from {_saveDataPath}");
            _savedData = SaveDataLoader.Load(_saveDataPath);
        }

        protected override VisualElement CreateView()
        {
            // unity doesn't catch errors here so we have to wrap in try/catch
            try
            {
                return CreateViewInternal();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
        }

        private VisualElement CreateViewInternal()
        {
            var root = new VisualElement();
            root.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            root.style.height = Length.Percent(100);
            root.style.overflow = Overflow.Hidden;

            var summary = new VisualElement();
            _countLabel = AddLabelWithPadding(summary);
            _bytesLabel = AddLabelWithPadding(summary);
            _perSecondLabel = AddLabelWithPadding(summary);
            _perSecondLabel.tooltip = Names.PER_SECOND_TOOLTIP;
            root.Add(summary);
            summary.style.height = Length.Percent(100);
            summary.style.width = 180;
            summary.style.minWidth = 180;
            summary.style.maxWidth = 180;
            summary.style.borderRightColor = Color.white * .4f;//dark grey
            summary.style.borderRightWidth = 3;

            _toggleBox = new VisualElement();
            _toggleBox.style.position = Position.Absolute;
            _toggleBox.style.bottom = 5;
            _toggleBox.style.left = 5;
            _toggleBox.style.unityTextAlign = TextAnchor.LowerLeft;
            summary.Add(_toggleBox);

            _groupMsgToggle = new Toggle();
            _groupMsgToggle.text = "Group Messages";
            _groupMsgToggle.tooltip = "Groups Message by type";
            _groupMsgToggle.value = true;
            _groupMsgToggle.RegisterValueChangedCallback(_ => ReloadData());
            _toggleBox.Add(_groupMsgToggle);

            // todo allow selection of multiple frames
            //var frameSlider = new MinMaxSlider();
            //frameSlider.highLimit = 300;
            //frameSlider.lowLimit = 1;
            //frameSlider.value = Vector2.one;
            //frameSlider.RegisterValueChangedCallback(_ => Debug.Log(frameSlider.value));
            //_toggleBox.Add(frameSlider);

            _debugToggle = new Toggle();
            _debugToggle.text = "Show Fake Messages";
            _debugToggle.tooltip = "Adds fakes message to table to debug layout of table";
            _debugToggle.value = false;
            _debugToggle.RegisterValueChangedCallback(_ => ReloadData());
            _toggleBox.Add(_debugToggle);
#if MIRAGE_PROFILER_DEBUG
            _debugToggle.style.display = DisplayStyle.Flex;
#else
            _debugToggle.style.display = DisplayStyle.None;
#endif


            var sorter = new TableSorter(this);
            _messageView = new MessageView(_columns, sorter, root);

            // Populate the label with the current data for the selected frame. 
            ReloadData();

            // Be notified when the selected frame index in the Profiler Window changes, so we can update the label.
            ProfilerWindow.SelectedFrameIndexChanged += FrameIndexChanged;

            return root;
        }

        internal void Sort(SortHeader header)
        {
            _savedData.SetSortHeader(header);
            SortFromSaveData();
        }

        private void SortFromSaveData()
        {
            var (sortHeader, sortMode) = _savedData.GetSortHeader(_columns);
            _messageView.Sort(sortHeader, sortMode);
        }

        private static Label AddLabelWithPadding(VisualElement parent)
        {
            var label = new Label() { style = { paddingTop = 8, paddingLeft = 8 } };
            parent.Add(label);
            return label;
        }

        private void FrameIndexChanged(long selectedFrameIndex)
        {
            // Update the label with the current data for the newly selected frame.
            ReloadData();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            // Unsubscribe from the Profiler window event that we previously subscribed to.
            ProfilerWindow.SelectedFrameIndexChanged -= FrameIndexChanged;

            Debug.Log($"Save to {_saveDataPath}");
            SaveDataLoader.Save(_saveDataPath, _savedData);

            base.Dispose(disposing);
        }

        private void ReloadData()
        {
            SetSummary(_countLabel, _names.Count);
            SetSummary(_bytesLabel, _names.Bytes);
            SetSummary(_perSecondLabel, _names.PerSecond);

            ReloadMessages();
        }

        private void SetSummary(Label label, string counterName)
        {
            var frame = (int)ProfilerWindow.selectedFrameIndex;
            var category = ProfilerCategory.Network.Name;
            var value = ProfilerDriver.GetFormattedCounterValue(frame, category, counterName);

            // replace prefix
            var display = counterName.Replace("Received", "").Replace("Sent", "").Trim();
            label.text = $"{display}: {value}";
        }

        private void ReloadMessages()
        {
            _messageView.Clear();

            if (!TryGetMessages(out var messages))
            {
                AddCantLoadLabel();
                return;
            }

            if (messages.Count == 0)
            {
                AddNoMessagesLabel();
                return;
            }

            var frame = new Frame[1] {
                new Frame{ Messages = messages },
            };
            _messageView.Draw(frame, _groupMsgToggle.value);
            SortFromSaveData();
        }

        private bool TryGetMessages(out List<MessageInfo> messages)
        {
            if (_debugToggle.value)
            {
                messages = GenerateDebugMessages();
                return true;
            }

            messages = null;
            var counter = _counterProvider.GetCountRecorder();
            if (counter == null)
                return false;

            var frameIndexStr = ProfilerDriver.GetFormattedCounterValue((int)ProfilerWindow.selectedFrameIndex, ProfilerCategory.Network.Name, Names.INTERNAL_FRAME_COUNTER);
            var frameIndex = 0;
            if (!string.IsNullOrEmpty(frameIndexStr))
                frameIndex = int.Parse(frameIndexStr);

            var frame = counter._frames[frameIndex];
            messages = frame.Messages;

            return true;
        }

        private static List<MessageInfo> GenerateDebugMessages()
        {
            var messages = new List<MessageInfo>();
            var order = 0;
            for (var i = 0; i < 5; i++)
            {
                messages.Add(NewInfo(order++, new RpcMessage { netId = (uint)i }, 20 + i, 5));
                messages.Add(NewInfo(order++, new SpawnMessage { netId = (uint)i }, 80 + i, 1));
                messages.Add(NewInfo(order++, new SpawnMessage { netId = (uint)i }, 60 + i, 4));
                messages.Add(NewInfo(order++, new NetworkPingMessage { }, 4, 1));

                static MessageInfo NewInfo(int order, object msg, int bytes, int count)
                {
#if MIRAGE_DIAGNOSTIC_INSTANCE
                        return new MessageInfo(null, msg, bytes, count);
#else
                    return new MessageInfo(new NetworkDiagnostics.MessageInfo(msg, bytes, count), order);
#endif
                }
            }

            return messages;
        }


        private void AddCantLoadLabel()
        {
            var parent = _messageView.AddEmptyRow();
            var ele = AddLabelWithPadding(parent);
            ele.style.color = Color.red;
            ele.text = "Can not load messages! (Message list only visible in play mode)\nIMPORTANT: make sure NetworkProfilerBehaviour is setup in starting scene";
        }

        private void AddNoMessagesLabel()
        {
            var parent = _messageView.AddEmptyRow();
            var ele = AddLabelWithPadding(parent);
            ele.text = "No Messages";
        }
    }
}