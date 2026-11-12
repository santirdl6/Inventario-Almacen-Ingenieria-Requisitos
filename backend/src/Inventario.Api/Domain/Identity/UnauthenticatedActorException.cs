namespace Inventario.Api.Domain.Identity;

public sealed class UnauthenticatedActorException()
    : UnauthorizedAccessException("Se requiere un usuario autenticado existente para registrar la operación.");
