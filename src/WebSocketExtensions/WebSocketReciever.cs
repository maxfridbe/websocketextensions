using System;

namespace WebSocketExtensions
{
    public abstract class WebSocketReciever
    {
        internal readonly Action<string, bool> _logger;

        internal WebSocketReciever(Action<string, bool> logger)
        {
            _logger = logger;
        }

        internal void _logInfo(string msg)
        {
            _logger?.Invoke(msg, false);
        }

        internal void _logError(string msg)
        {
            _logger?.Invoke(msg, true);
        }
    }
}
