namespace RinhaBackend;

public class Pessoa
{
    public Guid? Id { get; set; }

    public string Apelido { get; init; }

    public string Nome { get; init; }

    public string Nascimento { get; init; }

    public List<string>? Stack { get; init; }
    
    internal static bool BasicamenteValida(Pessoa pessoa)
    {
        var atributosInvalidos = string.IsNullOrEmpty(pessoa.Nascimento)
                                 || !EhNascimentoValido(pessoa.Nascimento)
                                 || string.IsNullOrEmpty(pessoa.Nome)
                                 || pessoa.Nome.Length > 100
                                 || string.IsNullOrEmpty(pessoa.Apelido)
                                 || pessoa.Apelido.Length > 32;

        if (atributosInvalidos)
            return false;

        return (pessoa.Stack ?? Enumerable.Empty<string>()).All(item => !string.IsNullOrEmpty(item) && item.Length <= 32);
    }

    internal static bool EhNascimentoValido(string nascimento)
    {
        return DateOnly.TryParseExact(nascimento, "yyyy-MM-dd", out _);
    }    
}
