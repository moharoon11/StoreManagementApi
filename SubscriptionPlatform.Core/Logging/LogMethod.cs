using System.Runtime.CompilerServices;

namespace SubscriptionPlatform.Core.Logging;

public static class LogMethod
{
    public static string MethodName(this object _, [CallerMemberName] string memberName = "") => memberName;
}
