using System;
using System.Collections;
using System.Collections.Generic;

namespace HereToSlay
{
    /// <summary>
    /// Runs the engine's nested IEnumerator "coroutines" without depending on Unity,
    /// so the same rules code runs inside Unity and in headless simulations.
    /// Step() advances until the engine needs something from the host:
    /// an unanswered <see cref="ChoiceRequest"/>, a <see cref="Pause"/>, or nothing (finished).
    /// </summary>
    public sealed class EngineRunner
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private object waitingOn;

        public bool Finished => stack.Count == 0 && waitingOn == null;

        public EngineRunner(IEnumerator root)
        {
            stack.Push(root);
        }

        /// <summary>Call after the host finished showing a Pause.</summary>
        public void ClearPause()
        {
            if (waitingOn is Pause)
            {
                waitingOn = null;
            }
        }

        public object Step(int maxIterations = 100000)
        {
            for (int guard = 0; guard < maxIterations; guard++)
            {
                if (waitingOn != null)
                {
                    if (waitingOn is ChoiceRequest request && !request.Resolved)
                    {
                        return request;
                    }

                    if (waitingOn is Pause)
                    {
                        return waitingOn;
                    }

                    waitingOn = null;
                }

                if (stack.Count == 0)
                {
                    return null;
                }

                IEnumerator top = stack.Peek();
                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception)
                {
                    stack.Clear();
                    throw;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }

                object current = top.Current;
                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else if (current is ChoiceRequest || current is Pause)
                {
                    waitingOn = current;
                }
            }

            throw new InvalidOperationException("Engine did not yield control (possible infinite loop).");
        }
    }
}
