using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Infrastructure.Extensions;
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
    [EnableRateLimiting(RateLimitExtensions.BotCreation)]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BotDto>> Create(CreateBotRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateBotCommand(request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.AvatarUrl, request.Aliases, userId, request.IsPublic, request.ChatId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The public bots; with <paramref name="mine"/>, only the ones the caller made.</summary>
    [HttpGet]
    [ProducesResponseType<List<BotDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BotDto>>> GetAll([FromQuery] bool mine = false, CancellationToken ct = default)
    {
        Guid? createdById = mine ? await userService.GetCurrentUserIdAsync(ct) : null;
        var result = await sender.Send(new GetBotsQuery(createdById), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{botId:guid}")]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BotDto>> Get(Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetBotQuery(botId, userId), ct);
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
