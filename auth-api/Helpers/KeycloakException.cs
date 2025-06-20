using System;

namespace ExamApp.Api.Helpers;

public class KeycloakException : Exception
{
    public int StatusCode { get; }

    public KeycloakException(string message, int statusCode = 500)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public KeycloakException(string message, Exception inner, int statusCode = 500)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

