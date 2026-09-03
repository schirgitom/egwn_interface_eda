using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Domain;
using Microsoft.AspNetCore.Mvc;

namespace EGWNInterfaceEda.Api;

[ApiController]
[Route("api/readings")]
public sealed class ReadingsController(IEdaReadingOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("meter")]
    public async Task<ActionResult<EdaReadingTriggerResponse>> TriggerMeterReading([FromBody] EdaTriggerRequest request, CancellationToken cancellationToken) =>
        Ok(await orchestrator.TriggerMeterReadingAsync(request, cancellationToken));

    [HttpPost("meter/{meterId}")]
    public async Task<ActionResult<EdaReadingTriggerResponse>> TriggerMeterReadingByMeterId(
        [FromRoute] string meterId,
        [FromBody] EdaTriggerRequest request,
        CancellationToken cancellationToken) =>
        Ok(await orchestrator.TriggerMeterReadingAsync(request with { MeterId = meterId }, cancellationToken));

    [HttpPost("kpi")]
    public async Task<ActionResult<EdaReadingTriggerResponse>> TriggerKpiReading([FromBody] EdaTriggerRequest request, CancellationToken cancellationToken) =>
        Ok(await orchestrator.TriggerKpiReadingAsync(request, cancellationToken));

    [HttpPost("kpi/{meterId}")]
    public async Task<ActionResult<EdaReadingTriggerResponse>> TriggerKpiReadingByMeterId(
        [FromRoute] string meterId,
        [FromBody] EdaTriggerRequest request,
        CancellationToken cancellationToken) =>
        Ok(await orchestrator.TriggerKpiReadingAsync(request, cancellationToken));
}
