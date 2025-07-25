using System;
using System.Collections.Generic;
using System.Linq;

namespace Rnd.Windows
{
    public class HistoryStack<State>
    {
        private List<State>     m_back    = new List<State>();
        private Stack<State>    m_forward = new Stack<State>();

        public int Limit { get; set; }

        public IEnumerable<State> UndoStack { get { return m_back; } }
        public IEnumerable<State> RedoStack { get { return m_forward; } }

        public HistoryStack (int limit = 50)
        {
            Limit = limit;
        }

        public State Undo (State current)
        {
            if (!CanUndo())
                return default(State);

            m_forward.Push (current);
            current = m_back.Last();
            m_back.RemoveAt (m_back.Count - 1);
            OnStateChanged();

            return current;
        }

        public State Redo (State current)
        {
            if (!CanRedo())
                return default(State);

            m_back.Add (current);
            current = m_forward.Pop();
            OnStateChanged();

            return current;
        }

        public bool CanUndo ()
        {
            return m_back.Any();
        }

        public bool CanRedo ()
        {
            return m_forward.Any();
        }

        public void Push (State current)
        {
            m_back.Add (current);
            if (m_back.Count > Limit)
                m_back.RemoveRange (0, m_back.Count - Limit);

            m_forward.Clear();
            OnStateChanged();
        }

        public void Clear ()
        {
            if (m_back.Any() || m_forward.Any())
            {
                m_back.Clear();
                m_forward.Clear();
                OnStateChanged();
            }
        }

        public event EventHandler StateChanged;

        private void OnStateChanged ()
        {
            if (StateChanged != null)
                StateChanged (this, EventArgs.Empty);
        }
    }
}
