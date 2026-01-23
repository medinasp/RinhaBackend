using Microsoft.EntityFrameworkCore;
using RinhaBackend;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:80");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSwaggerGen();

// CONFIGURAÇÃO DO DBCONTEXT COM RETRY
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(30);
    });
});

var app = builder.Build();

// ========== MIGRATION COM LOCK ==========
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    Console.WriteLine("🔄 Iniciando verificação do banco de dados...");
    
    try
    {
        // 1. Tenta conectar ao banco
        if (!await dbContext.Database.CanConnectAsync())
        {
            Console.WriteLine("⚠️  Não foi possível conectar ao banco. Tentando criar...");
            await dbContext.Database.EnsureCreatedAsync();
        }
        
        // 2. Verifica se há migrations pendentes
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            Console.WriteLine($"📦 Aplicando {pendingMigrations.Count()} migrations pendentes...");
            
            // Aplica migrations com timeout
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await dbContext.Database.MigrateAsync(cts.Token);
            
            Console.WriteLine("✅ Migrations aplicadas com sucesso!");
        }
        else
        {
            Console.WriteLine("✅ Nenhuma migration pendente.");
        }
        
        // 3. Verifica se a tabela existe
        var tableExists = await dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'Pessoas'")
            .FirstOrDefaultAsync();
        
        if (tableExists == 0)
        {
            Console.WriteLine("⚠️  Tabela 'Pessoas' não encontrada. Recriando banco...");
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "55P03") // lock_timeout
    {
        Console.WriteLine("⏳ Outra instância está aplicando migrations. Aguardando...");
        await Task.Delay(5000);
        
        // Tenta novamente
        await dbContext.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Erro durante migration: {ex.Message}");
        
        // Último recurso: cria o banco do zero
        try
        {
            Console.WriteLine("🔄 Tentando criar banco do zero...");
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
            Console.WriteLine("✅ Banco criado com sucesso!");
        }
        catch (Exception ex2)
        {
            Console.WriteLine($"💥 Falha crítica: {ex2.Message}");
            throw;
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => 
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return Results.Ok($"Healthy - DB: {(db.Database.CanConnect() ? "Connected" : "Disconnected")}");
    }
    catch
    {
        return Results.Ok("Healthy - DB Check Failed");
    }
});

app.MapGet("/getAllPessoa", async (AppDbContext db) =>
{
    var pessoas = await db.Pessoas.ToListAsync();
    return Results.Ok(pessoas);
});

app.MapGet("/pessoas/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
{
    var pessoa = await db.Pessoas
        .AsNoTracking()  // ADICIONE ESTA LINHA
        .FirstOrDefaultAsync(p => p.Id == id, ct);  // Mude de FindAsync para FirstOrDefaultAsync
    
    return pessoa is null ? Results.NotFound() : Results.Ok(pessoa);
});

app.MapGet("/pessoas", async (string t, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(t))
        return Results.BadRequest("Parâmetro 't' é obrigatório");

    var termo = t.ToLower();
    
    // Carrega tudo e filtra em memória
    var todasPessoas = await db.Pessoas
        .AsNoTracking()
        .Take(1000) // Limita pra não travar se tiver muitos dados
        .ToListAsync(ct);

    var pessoas = todasPessoas
        .Where(p =>
            p.Apelido.ToLower().Contains(termo) ||
            p.Nome.ToLower().Contains(termo) ||
            (p.Stack != null && p.Stack.Any(s => s.ToLower().Contains(termo))))
        .Take(50)
        .ToList();

    return Results.Ok(pessoas);
});

app.MapGet("/contagem-pessoas", async (AppDbContext db) =>
{
    var count = await db.Pessoas.CountAsync();
    return Results.Ok(count.ToString());
});

app.MapPost("/pessoas", async (Pessoa pessoa, AppDbContext db) =>
{
    if (!Pessoa.BasicamenteValida(pessoa))
        return Results.UnprocessableEntity();

    pessoa.Id = Guid.NewGuid();

    try
    {
        db.Pessoas.Add(pessoa);
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        return Results.UnprocessableEntity();
    }

    // EXATAMENTE como pede as regras: header Location e corpo vazio/qualquer
    return Results.Created($"/pessoas/{pessoa.Id}", new { id = pessoa.Id });
});

app.Run();