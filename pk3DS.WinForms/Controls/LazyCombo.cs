using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace pk3DS.WinForms.Controls
{
    /// <summary>
    /// A combo whose list is long (items, species, moves) costs about 0.25 ms per item to create on this setup, so an
    /// editor with 10,000 combo items took over a second to open. A lazy combo holds only its selected entry until the
    /// user touches it (mouse, key, drop-down), then it gets the full list. The selected position is kept here, so
    /// read and write it through <see cref="GetIndex"/> / <see cref="SetIndex"/>, never through SelectedIndex.
    /// </summary>
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

        /// <summary>True while <see cref="Expand"/> is refilling the list. Its Clear() and re-select raise SelectedIndexChanged
        /// for the entry that is already showing; a handler with side effects (saving, prompting) should skip those.</summary>
        public static bool IsExpanding(ComboBox cb) => States.TryGetValue(cb, out var st) && st.Expanding;

        /// <summary>Replaces one entry's text (e.g. a renamed trainer) in the stored list and, if shown, in the combo.</summary>
        public static void SetItem(ComboBox cb, int index, object item)
        {
            if (!States.TryGetValue(cb, out var st)) { cb.Items[index] = item; return; }
            st.Items[index] = item;
            if (st.Full) cb.Items[index] = item;
            else if (st.Index == index && cb.Items.Count == 1) cb.Items[0] = item;
        }

        /// <summary>Adds entries at the end of the list without touching the selection.</summary>
        public static void Append(ComboBox cb, object[] more)
        {
            if (!States.TryGetValue(cb, out var st)) { cb.Items.AddRange(more); return; }
            var all = new object[st.Items.Length + more.Length];
            st.Items.CopyTo(all, 0);
            more.CopyTo(all, st.Items.Length);
            st.Items = all;
            if (st.Full) cb.Items.AddRange(more);
        }

        /// <summary>Gives the combo its list (replacing any earlier one). A selection that is still inside the new list is kept: an editor that re-runs its setup would otherwise save -1 over the entry it is showing.</summary>
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

        /// <summary>Selects a list position; one outside the list clears the selection.</summary>
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

        /// <summary>Gives the combo its full list. Called when the user touches it; safe to call again.</summary>
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
