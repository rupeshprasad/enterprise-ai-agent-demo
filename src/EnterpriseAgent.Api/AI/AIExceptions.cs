namespace EnterpriseAgent.Api.AI;

public sealed class AIConfigurationException(string message) : Exception(message);

public sealed class AIProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
