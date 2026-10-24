using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace pk3DS.WinForms.Controls
{
    public static class LazyCombo
    {
        private sealed class State
        {
            public object[] Items = [];
            public int Index = -1;
            public bool Full;
            public bool Expanding;
        }

        private static readonly ConditionalWeakTable<ComboBox, State> States = new();

        public static bool IsLazy(ComboBox cb) => States.TryGetValue(cb, out _);

        public static bool IsExpanding(ComboBox cb) => States.TryGetValue(cb, out var st) && st.Expanding;

        public static void SetItem(ComboBox cb, int index, object item)
        {
            if (!States.TryGetValue(cb, out var st)) { cb.Items[index] = item; return; }
            st.Items[index] = item;
            if (st.Full) cb.Items[index] = item;
            else if (st.Index == index && cb.Items.Count == 1) cb.Items[0] = item;
        }

        public static void Append(ComboBox cb, object[] more)
        {
            if (!States.TryGetValue(cb, out var st)) { cb.Items.AddRange(more); return; }
            var all = new object[st.Items.Length + more.Length];
            st.Items.CopyTo(all, 0);
            more.CopyTo(all, st.Items.Length);
            st.Items = all;
            if (st.Full) cb.Items.AddRange(more);
        }

        public static void Attach(ComboBox cb, object[] items)
        {
            if (!States.TryGetValue(cb, out var st))
            {
                st = new State();
                States.Add(cb, st);
                void Open(object s, EventArgs e) => Expand(cb);
                cb.MouseDown += Open;
                cb.KeyDown += Open;
                cb.DropDown += Open;
                cb.MouseWheel += Open;
            }
            int keep = st.Index;
            st.Items = items;
            st.Index = -1;
            st.Full = false;
            cb.Items.Clear();
            if (keep >= 0 && keep < items.Length) SetIndex(cb, keep);
        }

        public static int Count(ComboBox cb) => States.TryGetValue(cb, out var st) ? st.Items.Length : cb.Items.Count;

        public static int GetIndex(ComboBox cb) =>
            !States.TryGetValue(cb, out var st) ? cb.SelectedIndex : st.Full ? cb.SelectedIndex : st.Index;

        public static void SetIndex(ComboBox cb, int index)
        {
            if (!States.TryGetValue(cb, out var st)) { cb.SelectedIndex = index; return; }
            if (index < 0 || index >= st.Items.Length) index = -1;
            st.Index = index;
            if (st.Full)
            {
                cb.SelectedIndex = index;
                return;
            }
            if (index < 0)
            {
                if (cb.Items.Count > 0) cb.Items.Clear();
                return;
            }
            if (!(cb.Items.Count == 1 && ReferenceEquals(cb.Items[0], st.Items[index])))
            {
                cb.Items.Clear();
                cb.Items.Add(st.Items[index]);
            }
            cb.SelectedIndex = 0;
        }

        public static void Expand(ComboBox cb)
        {
            if (!States.TryGetValue(cb, out var st) || st.Full) return;
            // Full stays false until the refill is done: Clear() raises SelectedIndexChanged with -1, and a handler
            // that reads GetIndex then must still get the stored position.
            st.Expanding = true;
            try
            {
                cb.BeginUpdate();
                cb.Items.Clear();
                cb.Items.AddRange(st.Items);
                cb.SelectedIndex = st.Index;
                cb.EndUpdate();
                st.Full = true;
            }
            finally { st.Expanding = false; }
        }
    }
}
