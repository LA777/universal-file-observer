import { HttpErrorResponse } from '@angular/common/http';
import { DialogData } from '../models/models';

/**
 * What the user was doing when the call failed, so the popup can name it.
 */
export interface HttpErrorContext {
  /** The attempted action, phrased to follow "Could not": 'open the folder'. */
  action: string;
  /** What was being acted on - a folder path, a snapshot name. */
  target?: string;
}

/**
 * Turns a failed HTTP call into a popup a user can act on.
 *
 * The popup used to show `error.error` verbatim. That is empty for every status the
 * server returns without a body, and a whole HTML error page for an unhandled
 * exception - so opening a folder that had been deleted produced a box with nothing
 * in it. Everything here has a message and a status regardless of what came back.
 */
export function describeHttpError(error: unknown, context: HttpErrorContext): DialogData {
  const attempt = context.target
    ? `Could not ${context.action} "${context.target}".`
    : `Could not ${context.action}.`;

  if (!(error instanceof HttpErrorResponse)) {
    return {
      title: 'Error',
      severity: 'error',
      message: attempt,
      hint: 'Something went wrong before the request reached the server.',
      details: readNonHttpDetails(error),
    };
  }

  const serverMessage = readServerMessage(error);
  const explanation = explainStatus(error.status, context);

  return {
    title: 'Error',
    severity: 'error',
    // The server's own sentence is already written for the user and names the exact
    // path, so it wins over the generic one derived from the status code.
    message: serverMessage ? `${attempt} ${serverMessage}` : `${attempt} ${explanation.message}`,
    // The generic hint only earns its place under the generic message. Beneath the
    // server's own sentence it restates it - a 404 for a deleted folder would say
    // "may have been renamed, moved, or deleted" twice, and a 404 for an unplugged
    // drive would say it once, wrongly.
    hint: serverMessage ? undefined : explanation.hint,
    details: buildDetails(error, serverMessage),
  };
}

/** A plain informational popup, so both kinds of dialog are built the same way. */
export function describeInfo(message: string, title: string = 'Info'): DialogData {
  return { title, message, severity: 'info' };
}

interface StatusExplanation {
  message: string;
  hint?: string;
}

function explainStatus(status: number, context: HttpErrorContext): StatusExplanation {
  switch (status) {
    // Angular reports a request that never got a response - server down, network
    // gone, TLS refused - as status 0.
    case 0:
      return {
        message: 'The UFO server did not respond.',
        hint: 'Check that the back end is running, then try again.',
      };
    case 400:
      return {
        message: 'The server rejected the request as invalid.',
        hint: 'Check the value you entered and try again.',
      };
    case 401:
      return {
        message: 'Your session is no longer valid.',
        hint: 'Sign in again to continue.',
      };
    case 403:
      return {
        message: 'Access is denied.',
        hint:
          'The location is outside the folders this server is allowed to read, or the '
          + 'account running UFO has no permission to open it.',
      };
    case 404:
      return {
        message: 'It was not found.',
        hint: 'It may have been renamed, moved, or deleted since the list was loaded.',
      };
    case 408:
    case 504:
      return {
        message: 'The server took too long to answer.',
        hint: 'A slow drive or a very large folder can cause this. Try again.',
      };
    default:
      return status >= 500
        ? {
            message: 'The server could not complete the request.',
            hint: 'The server log has the full error.',
          }
        : { message: `The server answered with status ${status}.` };
  }
}

/**
 * The server's own explanation, when it sent one worth showing.
 *
 * The API returns plain-string bodies for its failures, but a proxy, a developer
 * exception page or a ProblemDetails payload can arrive here too - none of which
 * belong in a sentence shown to the user.
 */
function readServerMessage(error: HttpErrorResponse): string {
  const body = error.error;

  if (typeof body === 'string') {
    const text = body.trim();

    // An HTML error page, or a wall of text: neither is a message. It still reaches
    // the user, but as technical detail rather than as the explanation.
    if (!text || text.startsWith('<') || text.length > 400) {
      return '';
    }

    return text;
  }

  if (body && typeof body === 'object') {
    const problem = body as Record<string, unknown>;

    // ASP.NET Core model validation: { errors: { Path: ['The Path field is required.'] } }
    const validationErrors = problem['errors'];
    if (validationErrors && typeof validationErrors === 'object') {
      const firstMessage = Object.values(validationErrors as Record<string, unknown>)
        .flatMap(messages => (Array.isArray(messages) ? messages : [messages]))
        .find(message => typeof message === 'string' && message.trim().length > 0);

      if (typeof firstMessage === 'string') {
        return firstMessage.trim();
      }
    }

    for (const key of ['detail', 'message', 'title']) {
      const candidate = problem[key];
      if (typeof candidate === 'string' && candidate.trim().length > 0) {
        return candidate.trim();
      }
    }
  }

  return '';
}

/** Everything a bug report needs, kept out of the way until asked for. */
function buildDetails(error: HttpErrorResponse, alreadyShownMessage: string): string {
  const lines: string[] = [
    `Status: ${error.status}${error.statusText ? ' ' + error.statusText : ''}`,
  ];

  if (error.url) {
    lines.push(`Request: ${error.url}`);
  }

  const body = error.error;
  const rawBody =
    typeof body === 'string'
      ? body.trim()
      : body instanceof ProgressEvent || body instanceof Error
        ? error.message
        : body
          ? safeStringify(body)
          : '';

  // No point repeating a short server sentence that is already the popup's message.
  if (rawBody && rawBody !== alreadyShownMessage) {
    lines.push(`Response: ${rawBody}`);
  }

  return lines.join('\n');
}

function readNonHttpDetails(error: unknown): string {
  if (error instanceof Error) {
    return `${error.name}: ${error.message}`;
  }

  return typeof error === 'string' ? error : safeStringify(error);
}

function safeStringify(value: unknown): string {
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}
