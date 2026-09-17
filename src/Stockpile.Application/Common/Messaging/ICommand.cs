using MediatR;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Common.Messaging;

/// <summary>A state-changing request. The transaction behavior wraps these and only these.</summary>
public interface ICommand : IRequest<Result>;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A read-only request. Never wrapped in a transaction, never enqueues notifications.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
