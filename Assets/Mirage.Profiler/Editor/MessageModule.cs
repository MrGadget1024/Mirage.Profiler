using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mirage.NetworkProfiler.ModuleGUI
{
    [System.Serializable]
    [ProfilerModuleMetadata(ModuleNames.SENT)]
    public class SentModule : ProfilerModule
    {
        static readonly ProfilerCounterDescriptor[] k_Counters = new ProfilerCounterDescriptor[]
        {
            new ProfilerCounterDescriptor(Names.SENT_COUNT, Counters.Category),
            new ProfilerCounterDescriptor(Names.SENT_BYTES, Counters.Category),
            new ProfilerCounterDescriptor(Names.SENT_PER_SECOND, Counters.Category),
        };

        public SentModule() : base(k_Counters) { }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            var names = new MessageViewController.CounterNames(
                Names.SENT_COUNT,
                Names.SENT_BYTES,
                Names.SENT_PER_SECOND
            );

            return new MessageViewController(ProfilerWindow, names);
        }
    }

    [System.Serializable]
    [ProfilerModuleMetadata(ModuleNames.RECEIVED)]
    public class ReceivedModule : ProfilerModule
    {
        static readonly ProfilerCounterDescriptor[] k_Counters = new ProfilerCounterDescriptor[]
        {
            new ProfilerCounterDescriptor(Names.RECEIVED_COUNT, Counters.Category),
            new ProfilerCounterDescriptor(Names.RECEIVED_BYTES, Counters.Category),
            new ProfilerCounterDescriptor(Names.RECEIVED_PER_SECOND, Counters.Category),
        };

        public ReceivedModule() : base(k_Counters) { }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            var names = new MessageViewController.CounterNames(
                Names.RECEIVED_COUNT,
                Names.RECEIVED_BYTES,
                Names.RECEIVED_PER_SECOND
            );

            return new MessageViewController(ProfilerWindow, names);
        }
    }

    public sealed class MessageViewController : ProfilerModuleViewController
    {
        readonly CounterNames _names;

        Label _countLabel;
        Label _bytesLabel;
        Label _perSecondLabel;

        readonly Columns columns = new Columns();
        Table table;

        public MessageViewController(ProfilerWindow profilerWindow, CounterNames names) : base(profilerWindow)
        {
            _names = names;
        }

        protected override VisualElement CreateView()
        {
            var root = new VisualElement();
            root.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            root.style.height = Length.Percent(100);

            VisualElement labels = CreateLabels();
            root.Add(labels);
            labels.style.height = Length.Percent(100);
            labels.style.width = 180;
            labels.style.borderRightColor = Color.white * .4f;//dark grey
            labels.style.borderRightWidth = 2;

            table = new Table(columns);
            root.Add(table.VisualElement);

            // Populate the label with the current data for the selected frame. 
            ReloadData();

            // Be notified when the selected frame index in the Profiler Window changes, so we can update the label.
            ProfilerWindow.SelectedFrameIndexChanged += OnSelectedFrameIndexChanged;

            root.style.overflow = Overflow.Hidden;
            return root;
        }

        private VisualElement CreateLabels()
        {
            var dataView = new VisualElement();
            _countLabel = AddLabelWithPadding(dataView);
            _bytesLabel = AddLabelWithPadding(dataView);
            _perSecondLabel = AddLabelWithPadding(dataView);
            _perSecondLabel.tooltip = Names.PER_SECOND_TOOLTIP;
            return dataView;
        }

        static Label AddLabelWithPadding(VisualElement view)
        {
            var label = new Label() { style = { paddingTop = 8, paddingLeft = 8 } };
            view.Add(label);
            return label;
        }

        void OnSelectedFrameIndexChanged(long selectedFrameIndex)
        {
            // Update the label with the current data for the newly selected frame.
            ReloadData();
        }


        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            // Unsubscribe from the Profiler window event that we previously subscribed to.
            ProfilerWindow.SelectedFrameIndexChanged -= OnSelectedFrameIndexChanged;

            base.Dispose(disposing);
        }

        void ReloadData()
        {
            SetText(_countLabel, _names.Count);
            SetText(_bytesLabel, _names.Bytes);
            SetText(_perSecondLabel, _names.PerSecond);

            reloadMessages();
        }

        void SetText(Label label, string name)
        {
            int frame = (int)ProfilerWindow.selectedFrameIndex;
            string category = ProfilerCategory.Network.Name;
            string value = ProfilerDriver.GetFormattedCounterValue(frame, category, name);

            label.text = $"{name}: {value}";
        }


        private void reloadMessages()
        {
            table.Clear();

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

            foreach (NetworkDiagnostics.MessageInfo message in messages)
            {
                Row row = table.AddRow();
                row.AddElement(columns.FullName, message.message.GetType().FullName);
                row.AddElement(columns.TotalBytes, message.bytes * message.count);
                row.AddElement(columns.Count, message.count);
                row.AddElement(columns.BytesPerMessage, message.bytes);
                uint? netid = GetNetId(message);
                string netidStr = netid.HasValue ? netid.ToString() : "";
                row.AddElement(columns.NetId, netidStr);
            }
        }

        private void AddCantLoadLabel()
        {
            Row row = table.AddRow();
            Label ele = AddLabelWithPadding(row.VisualElement);
            ele.style.color = Color.red;
            ele.text = "Can not load messages! (Message list only visible in play mode)";
        }

        private void AddNoMessagesLabel()
        {
            Row row = table.AddRow();
            Label ele = AddLabelWithPadding(row.VisualElement);
            ele.text = "No Messages";
        }
        const bool DEBUG = true;

        private bool TryGetMessages(out List<NetworkDiagnostics.MessageInfo> messages)
        {
            if (DEBUG) {
                messages = new List<NetworkDiagnostics.MessageInfo>();

                messages.Add(new NetworkDiagnostics.MessageInfo());
                messages.Add(new NetworkDiagnostics.MessageInfo());
                messages.Add(new NetworkDiagnostics.MessageInfo());
                messages.Add(new NetworkDiagnostics.MessageInfo());
                messages.Add(new NetworkDiagnostics.MessageInfo());

                return true;
            }


            messages = null;
            if (NetworkProfilerBehaviour.sentCounter == null)
                return false;

            string frameIndexStr = ProfilerDriver.GetFormattedCounterValue((int)ProfilerWindow.selectedFrameIndex, ProfilerCategory.Network.Name, Names.INTERNAL_FRAME_COUNTER);
            int frameIndex = 0;
            if (!string.IsNullOrEmpty(frameIndexStr))
                frameIndex = int.Parse(frameIndexStr);

            Frame frame = NetworkProfilerBehaviour.sentCounter.frames[frameIndex];
            int count = frame.Messages.Count;

            messages = frame.Messages;
            return true;
        }

        static uint? GetNetId(object message)
        {
            switch (message)
            {
                case ServerRpcMessage msg: return msg.netId;
                case ServerRpcWithReplyMessage msg: return msg.netId;
                case RpcMessage msg: return msg.netId;
                case SpawnMessage msg: return msg.netId;
                case RemoveAuthorityMessage msg: return msg.netId;
                case ObjectDestroyMessage msg: return msg.netId;
                case ObjectHideMessage msg: return msg.netId;
                case UpdateVarsMessage msg: return msg.netId;
                default: return default;
            }
        }

        public struct CounterNames
        {
            public readonly string Count;
            public readonly string Bytes;
            public readonly string PerSecond;

            public CounterNames(string count, string bytes, string perSecond)
            {
                Count = count;
                Bytes = bytes;
                PerSecond = perSecond;
            }
        }
    }

    sealed class Columns : IEnumerable<ColumnInfo>
    {
        public ColumnInfo FullName = new ColumnInfo("Message", 300);
        public ColumnInfo TotalBytes = new ColumnInfo("Total Bytes", 150);
        public ColumnInfo Count = new ColumnInfo("Count", 150);
        public ColumnInfo BytesPerMessage = new ColumnInfo("Bytes Per Message", 150);
        public ColumnInfo NetId = new ColumnInfo("Net id", 150);

        public IEnumerator<ColumnInfo> GetEnumerator()
        {
            yield return FullName;
            yield return TotalBytes;
            yield return Count;
            yield return BytesPerMessage;
            yield return NetId;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
