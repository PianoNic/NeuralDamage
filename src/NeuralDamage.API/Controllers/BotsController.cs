using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class BotsController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(CreateBotRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateBotCommand(request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.AvatarUrl, request.Aliases, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet]
    [ProducesResponseType<List<BotDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BotDto>>> GetAll(CancellationToken ct)
    {
        var result = await sender.Send(new GetBotsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{botId:guid}")]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BotDto>> Get(Guid botId, CancellationToken ct)
    {
        var result = await sender.Send(new GetBotQuery(botId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{botId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Update(Guid botId, UpdateBotRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateBotCommand(botId, userId, request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.AvatarUrl, request.Aliases, request.IsActive), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpDelete("{botId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteBotCommand(botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("models")]
    [ProducesResponseType<List<OpenRouterModel>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OpenRouterModel>>> ListModels(CancellationToken ct)
    {
        var result = await sender.Send(new ListOpenRouterModelsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
