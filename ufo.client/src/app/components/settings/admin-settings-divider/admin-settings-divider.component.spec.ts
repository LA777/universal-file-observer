import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminSettingsDividerComponent } from './admin-settings-divider.component';

describe('AdminSettingsDividerComponent', () => {
  let fixture: ComponentFixture<AdminSettingsDividerComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AdminSettingsDividerComponent] });
    fixture = TestBed.createComponent(AdminSettingsDividerComponent);
    fixture.detectChanges();
  });

  it('says the settings below it are for administrators', () => {
    const rendered = fixture.nativeElement as HTMLElement;

    expect(rendered.textContent).toContain('Administrator settings');
  });

  it('explains that these settings are server-wide rather than per-account', () => {
    const rendered = fixture.nativeElement as HTMLElement;

    // The label alone reads as "you cannot touch this"; the hint is what says
    // why, which is the part a reader actually needs.
    expect(rendered.textContent).toContain('everyone who uses it');
    expect(rendered.textContent).toContain('Only administrators can change it');
  });

  it('is announced as a separator rather than as a heading', () => {
    const separator = (fixture.nativeElement as HTMLElement).querySelector('[role="separator"]');

    expect(separator).not.toBeNull();
    expect(separator!.getAttribute('aria-label')).toBe('Administrator settings');
  });

  it('hides the decorative icon from assistive technology', () => {
    const icon = (fixture.nativeElement as HTMLElement).querySelector('mat-icon');

    // The icon repeats the label beside it; announcing it would read the same
    // thing twice.
    expect(icon!.getAttribute('aria-hidden')).toBe('true');
  });
});
