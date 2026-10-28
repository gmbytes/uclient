using System;

namespace Client.Contracts
{
    public enum SessionState { Idle, Connecting, Authenticating, Authenticated, Disconnected }
    public enum LoginError { None, InvalidInput, Busy, ConnectFailed, Timeout, Cancelled, Rejected, ProtocolError, Disconnected }

    public sealed class LoginInput
    {
        public string Credential { get; }
        public string ResumeToken { get; }

        public LoginInput(string credential, string resumeToken)
        {
            if (string.IsNullOrWhiteSpace(credential) == string.IsNullOrWhiteSpace(resumeToken))
                throw new ArgumentException("Exactly one login input is required.");
            Credential = credential;
            ResumeToken = resumeToken;
        }
    }

    public sealed class SessionIdentity
    {
        public string PlayerId { get; }
        public string SessionId { get; }
        public ulong Epoch { get; }
        public string ResumeToken { get; }
        public ulong ResumeTokenTtlMs { get; }

        public SessionIdentity(string playerId, string sessionId, ulong epoch, string resumeToken, ulong resumeTokenTtlMs)
        {
            PlayerId = playerId;
            SessionId = sessionId;
            Epoch = epoch;
            ResumeToken = resumeToken;
            ResumeTokenTtlMs = resumeTokenTtlMs;
        }
    }

    public sealed class LoginResult
    {
        public bool Succeeded { get; }
        public LoginError Error { get; }
        public uint ServerErrorCode { get; }
        public SessionIdentity Identity { get; }
        public string SafeMessage { get; }

        public LoginResult(bool succeeded, LoginError error, uint serverErrorCode, SessionIdentity identity, string safeMessage)
        {
            Succeeded = succeeded;
            Error = error;
            ServerErrorCode = serverErrorCode;
            Identity = identity;
            SafeMessage = safeMessage;
        }
    }
}
