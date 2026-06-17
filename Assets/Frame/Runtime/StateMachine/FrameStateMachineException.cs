using System;

namespace Frame.StateMachine
{
    /// <summary>
    /// Thrown when a state machine operation fails: an unknown id in strict mode, or an
    /// exception escaping a state callback (Enter/Tick/Exit/etc.). The original error is
    /// preserved as <see cref="Exception.InnerException"/> so the call site keeps full context
    /// instead of the machine silently swallowing it.
    /// </summary>
    public sealed class FrameStateMachineException : Exception
    {
        public FrameStateMachineException(string message)
            : base(message)
        {
        }

        public FrameStateMachineException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
