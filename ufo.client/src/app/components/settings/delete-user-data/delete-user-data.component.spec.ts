import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { DeleteUserDataComponent, UserDataKind } from './delete-user-data.component';
import { FsItemFlagsService } from '../../../services/fs-item-flags.service';
import { FsItemRatingsService } from '../../../services/fs-item-ratings.service';
import { TagsService } from '../../../services/tags.service';
import { KeyBindingsService } from '../../../services/key-bindings.service';
import { ThemeService } from '../../../services/theme.service';

describe('DeleteUserDataComponent', () => {
  let fixture: ComponentFixture<DeleteUserDataComponent>;
  let component: DeleteUserDataComponent;
  let httpMock: HttpTestingController;

  /** What the confirm dialog answers; false is every way of declining. */
  let dialogAnswer: boolean;
  let dialogOpen: jasmine.Spy;

  const URLS: Record<UserDataKind, string> = {
    snapshots: '/api/userdata/snapshots',
    filesystem: '/api/userdata/filesystem',
    settings: '/api/userdata/settings'
  };

  beforeEach(() => {
    localStorage.clear();
    dialogAnswer = true;
    dialogOpen = jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => of(dialogAnswer) }));

    TestBed.configureTestingModule({
      imports: [DeleteUserDataComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: MatDialog, useValue: { open: dialogOpen } }
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DeleteUserDataComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('theme-light', 'theme-dark');
  });

  const deletionOf = (kind: UserDataKind) => component.deletions.find(deletion => deletion.kind === kind)!;

  it('offers exactly the three deletions, in the order of the page', () => {
    expect(component.deletions.map(deletion => deletion.kind)).toEqual(['snapshots', 'filesystem', 'settings']);
  });

  it('asks before deleting and marks the question as destructive', () => {
    component.requestDeletion(deletionOf('snapshots'));

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const data = dialogOpen.calls.mostRecent().args[1].data;
    expect(data.isDestructive).toBeTrue();
    expect(data.confirmLabel).toBe('Delete snapshots');
    httpMock.expectOne(URLS.snapshots).flush({ message: 'Deleted 2 snapshot(s).' });
    httpMock.verify();
  });

  it('does nothing when the question is declined', () => {
    dialogAnswer = false;

    component.requestDeletion(deletionOf('snapshots'));

    httpMock.expectNone(URLS.snapshots);
    expect(component.isWorking).toBeFalse();
    expect(component.message).toBe('');
    httpMock.verify();
  });

  (['snapshots', 'filesystem', 'settings'] as UserDataKind[]).forEach(kind => {
    it(`deletes ${kind} with DELETE ${URLS[kind]} once confirmed`, () => {
      component.requestDeletion(deletionOf(kind));

      expect(component.workingKind).toBe(kind);
      const request = httpMock.expectOne(URLS[kind]);
      expect(request.request.method).toBe('DELETE');
      request.flush({ message: 'Deleted 3 item(s).' });

      expect(component.workingKind).toBeNull();
      expect(component.message).toContain(deletionOf(kind).successMessage);
      expect(component.message).toContain('Deleted 3 item(s).');
      expect(component.errorMessage).toBe('');
      // The shortcuts refresh after a settings delete is asserted in its own test.
      httpMock.match('/api/settings/shortcuts');
      httpMock.verify();
    });
  });

  it('reports a failed deletion and lets the user try again', () => {
    component.requestDeletion(deletionOf('filesystem'));
    httpMock.expectOne(URLS.filesystem).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.workingKind).toBeNull();
    expect(component.message).toBe('');
    expect(component.errorMessage).not.toBe('');

    component.requestDeletion(deletionOf('filesystem'));
    httpMock.expectOne(URLS.filesystem).flush({});

    expect(component.errorMessage).toBe('');
    httpMock.verify();
  });

  it('ignores a second click while a deletion is in flight', () => {
    component.requestDeletion(deletionOf('snapshots'));
    expect(component.isWorking).toBeTrue();

    component.requestDeletion(deletionOf('settings'));

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    httpMock.expectNone(URLS.settings);
    httpMock.expectOne(URLS.snapshots).flush({});
    httpMock.verify();
  });

  it('forgets the cached flags, ratings and tags after deleting file system data', () => {
    const flags = TestBed.inject(FsItemFlagsService);
    const ratings = TestBed.inject(FsItemRatingsService);
    const tags = TestBed.inject(TagsService);
    flags.flaggedPaths.set(new Set(['/data/report.pdf']));
    ratings.ratingsByPath.set(new Map([['/data/report.pdf', 7]]));
    tags.tags.set([{ id: 't1', name: 'Important', colorHex: '#00ff00' }]);
    tags.tagIdsByPath.set(new Map([['/data/report.pdf', ['t1']]]));

    component.requestDeletion(deletionOf('filesystem'));
    httpMock.expectOne(URLS.filesystem).flush({});

    expect(flags.flaggedPaths().size).toBe(0);
    expect(ratings.ratingsByPath().size).toBe(0);
    expect(tags.tags().length).toBe(0);
    expect(tags.tagIdsByPath().size).toBe(0);
    httpMock.verify();
  });

  it('keeps the cached marks when the file system delete fails', () => {
    const flags = TestBed.inject(FsItemFlagsService);
    flags.flaggedPaths.set(new Set(['/data/report.pdf']));

    component.requestDeletion(deletionOf('filesystem'));
    httpMock.expectOne(URLS.filesystem).flush('boom', { status: 500, statusText: 'Server Error' });

    // Nothing was deleted, so nothing should be forgotten.
    expect(flags.flaggedPaths().size).toBe(1);
    httpMock.verify();
  });

  it('puts the theme back to the default, re-reads the shortcuts and says so after deleting settings', () => {
    const themeService = TestBed.inject(ThemeService);
    const keyBindingsService = TestBed.inject(KeyBindingsService);
    themeService.applyTheme('light');
    keyBindingsService.keyBindings.set([{ actionId: 'files.copy', label: 'Copy', group: 'File operations', primaryKey: 'F9', secondaryKey: '', defaultPrimaryKey: 'F5', defaultSecondaryKey: '', isDefault: false }]);
    let announced = 0;
    component.settingsDeleted.subscribe(() => announced++);

    component.requestDeletion(deletionOf('settings'));
    httpMock.expectOne(URLS.settings).flush({});

    expect(themeService.currentTheme).toBe('dark');
    expect(announced).toBe(1);
    // The shortcuts are asked for again rather than trusted from the cache.
    const reload = httpMock.expectOne('/api/settings/shortcuts');
    reload.flush([{ actionId: 'files.copy', label: 'Copy', group: 'File operations', primaryKey: 'F5', secondaryKey: '', defaultPrimaryKey: 'F5', defaultSecondaryKey: '', isDefault: true }]);
    expect(keyBindingsService.keyBindings()[0].primaryKey).toBe('F5');
    httpMock.verify();
  });

  it('leaves the theme and the caches alone after deleting snapshots', () => {
    const themeService = TestBed.inject(ThemeService);
    const flags = TestBed.inject(FsItemFlagsService);
    themeService.applyTheme('light');
    flags.flaggedPaths.set(new Set(['/data/report.pdf']));

    component.requestDeletion(deletionOf('snapshots'));
    httpMock.expectOne(URLS.snapshots).flush({});

    expect(themeService.currentTheme).toBe('light');
    expect(flags.flaggedPaths().size).toBe(1);
    httpMock.verify();
  });
});
