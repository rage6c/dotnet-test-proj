namespace PgDuckDump.Services;

public sealed class InvalidConfigurationException(string message) : Exception(message);
