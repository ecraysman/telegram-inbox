import { HttpErrorResponse } from '@angular/common/http';

/** Turns an api failure (RFC 9457 ProblemDetails with `code`) into a Spanish UI message. */
export function describeSendError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = readCode(error.error);
    if (code === 'validation_failed' || error.status === 400) {
      return 'El mensaje no es válido: no puede estar vacío ni superar los 4096 caracteres.';
    }
    if (code === 'contact_not_found' || error.status === 404) {
      return 'La conversación ya no existe.';
    }
    if (error.status === 0) {
      return 'No hay conexión con el servidor. Revisá la red e intentá de nuevo.';
    }
  }
  return 'No se pudo enviar el mensaje. Intentá de nuevo.';
}

function readCode(body: unknown): string | null {
  if (typeof body === 'object' && body !== null && 'code' in body) {
    const code = (body as { code: unknown }).code;
    return typeof code === 'string' ? code : null;
  }
  return null;
}
