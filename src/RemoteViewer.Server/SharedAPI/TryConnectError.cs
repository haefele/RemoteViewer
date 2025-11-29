namespace RemoteViewer.Server.SharedAPI;

public enum TryConnectError
{
    ViewerNotFound,
    IncorrectUsernameOrPassword,
    CannotConnectToYourself,
}
