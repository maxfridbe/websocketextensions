using System;

namespace WebSocketExtensions
{
    public abstract class WebSocketReciever
    {
        private readonly Action<string, bool> _logger;

        public WebSocketReciever(Action<string, bool> logger)
        {
            _logger = logger;
        }

        public void _logInfo(string msg)
        {
            _logger?.Invoke(msg, false);
        }

        public void _logError(string msg)
        {
            _logger?.Invoke(msg, true);
        }
    }
}
