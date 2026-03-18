using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Dtos.Requests;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BotsController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateBotRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateBotCommand(request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.AvatarUrl, request.Aliases, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await sender.Send(new GetBotsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{botId:guid}")]
    public async Task<IActionResult> Get(Guid botId, CancellationToken ct)
    {
        var result = await sender.Send(new GetBotQuery(botId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{botId:guid}")]
    public async Task<IActionResult> Update(Guid botId, UpdateBotRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateBotCommand(botId, userId, request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.AvatarUrl, request.Aliases, request.IsActive), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpDelete("{botId:guid}")]
    public async Task<IActionResult> Delete(Guid botId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteBotCommand(botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken ct)
    {
        var result = await sender.Send(new ListOpenRouterModelsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
