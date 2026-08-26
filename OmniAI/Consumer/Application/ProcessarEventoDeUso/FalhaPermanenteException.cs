namespace Consumer.Application.ProcessarEventoDeUso;

public class FalhaPermanenteException : Exception
{
    public FalhaPermanenteException(string message) : base(message)
    {
    }
}
