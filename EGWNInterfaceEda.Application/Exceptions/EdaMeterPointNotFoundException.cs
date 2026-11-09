namespace EGWNInterfaceEda.Application.Exceptions;

public sealed class EdaMeterPointNotFoundException(string endpoint) : Exception(
    $"EDA did not find a meter point at '{endpoint}'.");
