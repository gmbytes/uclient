using System;
using System.Globalization;
using Client.Contracts;
namespace Client.Game.Login
{
    public static class DevelopmentCredentialProvider
    {
        // Local demo credentials only. Never use this provider for production authentication.
        public static LoginInput Create(string account)
        {
            if (string.IsNullOrEmpty(account) || !ulong.TryParse(account, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
                throw new ArgumentException("Account must be a positive unsigned integer.", nameof(account));
            return new LoginInput("demo:" + id.ToString(CultureInfo.InvariantCulture), null);
        }
    }
}
