using Shared;
using Xunit.Abstractions;

namespace Scenarios.Pipeline.Scheduler.Router;

public class RoteadorDeOfertasTests : IClassFixture<Fixture>
{
    private readonly Fixture           fixture;
    private readonly ITestOutputHelper output;
    private readonly PipelineDeOfertas pipeline;
    private readonly SemeadorDeRegras  semeador;

    public RoteadorDeOfertasTests(Fixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output  = output;
        pipeline     = new PipelineDeOfertas(fixture, output);
        semeador     = new SemeadorDeRegras(fixture, output);
    }

    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task OfertaConsignado_DeveSerRoteada_ParaFilaConsignado()
    {
        var oferta = new OfertaDeCredito("oferta-001", "consignado", Taxa: 1.2m, Valor: 10_000m);

        output.WriteLine($">>> Lambda.Invoke: invocando '{Fixture.NomeLambdaProdutora}' com oferta de segmento '{oferta.Segmento}'");
        output.WriteLine("    Regra: segmento=consignado → fila-consignado | a Lambda produtora publica na fila de entrada");
        output.WriteLine("    Pipeline: produtor-de-ofertas → fila-ofertas-credito → roteador-de-ofertas → fila-consignado");
        await pipeline.SubmeterOfertaAsync(oferta);

        output.WriteLine($">>> Polling SQS: aguardando até 30s pela oferta '{oferta.Id}' na fila '{Fixture.NomeFilaConsignado}'");
        await pipeline.VerificarRoteamentoAsync(oferta.Id, fixture.UrlFilaConsignado);

        output.WriteLine("    Oferta encontrada na fila consignado — regra de roteamento aplicada com sucesso");
    }

    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task OfertaSegmentoDesconhecido_NaoDeveChegar_NaFilaConsignado()
    {
        var oferta = new OfertaDeCredito("oferta-002", "pessoal", Taxa: 2.5m, Valor: 5_000m);

        output.WriteLine($">>> Lambda.Invoke: invocando '{Fixture.NomeLambdaProdutora}' com oferta de segmento '{oferta.Segmento}'");
        output.WriteLine("    Regra: segmento=pessoal não tem regra cadastrada → roteador descarta com aviso no stdout");
        output.WriteLine("    Verificação: fila-consignado NÃO deve receber esta oferta");
        await pipeline.SubmeterOfertaAsync(oferta);

        output.WriteLine($">>> AssertNever SQS: verificando durante 5s que oferta '{oferta.Id}' NÃO aparece na fila '{Fixture.NomeFilaConsignado}'");
        await pipeline.VerificarAusenciaDeRoteamentoAsync(oferta.Id, fixture.UrlFilaConsignado);

        output.WriteLine("    Oferta ausente na fila consignado — isolamento de segmentos funcionando corretamente");
    }

    [Fact]
    public async Task RegrasDeRoteamento_DevemEstarPresentes_NoDynamoDB()
    {
        output.WriteLine($">>> DynamoDB.Scan: lendo tabela de regras '{Fixture.NomeTabelaRegras}'");
        output.WriteLine("    Regras são semeadas no Fixture antes dos testes — verifica que a semeadura foi bem-sucedida");
        var regras = await semeador.ObterRegrasAsync();

        output.WriteLine($"    {regras.Count} regra(s) encontrada(s) — verificando presença do segmento 'consignado'");
        Assert.Contains(regras, r => r.Segmento == "consignado");

        output.WriteLine("    Regra 'consignado' presente — tabela de roteamento inicializada corretamente");
    }

    [Fact]
    public async Task AgendadorDeOfertas_DeveEstarConfigurado_ComTargetCorreto()
    {
        output.WriteLine($">>> EventBridge.GetSchedule: verificando configuração do agendador '{Fixture.NomeAgendador}'");
        output.WriteLine("    O agendador aciona a Lambda produtora a cada 20s em produção — verificamos a configuração sem esperar execução");
        var agendador = await fixture.ObterConfiguracaoDoAgendadorAsync();

        output.WriteLine($"    Expressão: '{agendador.Expressao}' | Alvo: '{agendador.FuncaoAlvo}'");
        Assert.Equal(Fixture.NomeLambdaProdutora, agendador.FuncaoAlvo);
        Assert.Equal("rate(20 seconds)", agendador.Expressao);

        output.WriteLine("    Agendador configurado corretamente — target e expressão validados");
    }
}
