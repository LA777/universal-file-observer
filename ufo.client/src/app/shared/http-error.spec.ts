import { HttpErrorResponse } from '@angular/common/http';
import { describeHttpError } from './http-error';

describe('describeHttpError', () => {
  const openFolder = { action: 'open the folder', target: 'C:\\missing' };

  it('uses the server sentence when the server sent one', () => {
    const error = new HttpErrorResponse({
      status: 404,
      statusText: 'Not Found',
      url: '/api/filesystem/folder',
      error: "The folder 'C:\\missing' does not exist. It may have been renamed, moved, or deleted.",
    });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).toContain('Could not open the folder "C:\\missing".');
    expect(dialogData.message).toContain('does not exist');
    expect(dialogData.severity).toBe('error');
    // The generic 404 hint would only restate the sentence the server already sent.
    expect(dialogData.hint).toBeUndefined();
  });

  it('keeps the generic hint when the server said nothing', () => {
    const error = new HttpErrorResponse({ status: 404, statusText: 'Not Found', error: null });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).toContain('It was not found.');
    expect(dialogData.hint).toContain('renamed, moved, or deleted');
  });

  it('explains the status when the response carries no body', () => {
    const error = new HttpErrorResponse({ status: 403, statusText: 'Forbidden', error: null });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).toContain('Access is denied.');
    expect(dialogData.hint).toContain('permission');
  });

  it('does not put an HTML error page in the message', () => {
    const error = new HttpErrorResponse({
      status: 500,
      statusText: 'Internal Server Error',
      error: '<!DOCTYPE html><html><body>Unhandled exception</body></html>',
    });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).not.toContain('<!DOCTYPE');
    expect(dialogData.message).toContain('The server could not complete the request.');
    expect(dialogData.details).toContain('<!DOCTYPE');
  });

  it('reads the message out of an ASP.NET validation payload', () => {
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: { errors: { Path: ['The Path field is required.'] } },
    });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).toContain('The Path field is required.');
  });

  it('names an unreachable server rather than showing status 0', () => {
    const error = new HttpErrorResponse({ status: 0, statusText: 'Unknown Error', error: new ProgressEvent('error') });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.message).toContain('The UFO server did not respond.');
    expect(dialogData.hint).toContain('back end is running');
  });

  it('always reports the status in the details', () => {
    const error = new HttpErrorResponse({ status: 404, statusText: 'Not Found', url: '/api/filesystem/folder' });

    const dialogData = describeHttpError(error, openFolder);

    expect(dialogData.details).toContain('Status: 404 Not Found');
    expect(dialogData.details).toContain('/api/filesystem/folder');
  });

  it('describes a failure that is not an HTTP response at all', () => {
    const dialogData = describeHttpError(new TypeError('boom'), openFolder);

    expect(dialogData.message).toBe('Could not open the folder "C:\\missing".');
    expect(dialogData.details).toContain('TypeError: boom');
  });
});
