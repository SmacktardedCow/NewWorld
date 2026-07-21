using System;

public class CPHInline
{
    private const string CITIZENSHIP_VARIABLE =
        "Main.Citizenship.Date";

    public bool Execute()
    {
        string login = "";
        string followDate = "";
        bool isFollowing = false;

        CPH.TryGetArg("followUserName", out login);
        CPH.TryGetArg("followDate", out followDate);
        CPH.TryGetArg("isFollowing", out isFollowing);

        if (string.IsNullOrWhiteSpace(login))
            CPH.TryGetArg("userName", out login);

        if (!isFollowing ||
            string.IsNullOrWhiteSpace(login) ||
            string.IsNullOrWhiteSpace(followDate))
        {
            CPH.LogWarn(
                "Citizenship date not saved. The target either " +
                "is not following, or the Get Follow Age Info " +
                "sub-action did not return a date."
            );

            return true;
        }

        login = login.Trim().ToLowerInvariant();
        followDate = followDate.Trim();

        CPH.SetTwitchUserVar(
            login,
            CITIZENSHIP_VARIABLE,
            followDate,
            true
        );

        CPH.LogInfo(
            "Citizenship date saved for " +
            login +
            ": " +
            followDate
        );

        return true;
    }
}
