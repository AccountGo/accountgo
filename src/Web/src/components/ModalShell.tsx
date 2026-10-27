import React, { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Nested modals share one lock so unlock only runs when the last one closes. */
let scrollLockCount = 0;
let previousBodyOverflow = '';
let previousHtmlOverflow = '';
let previousMainOverflow: string | null = null;
let previousBodyPaddingRight = '';

function acquireScrollLock() {
  if (scrollLockCount === 0) {
    const scrollbarGap = window.innerWidth - document.documentElement.clientWidth;
    previousBodyOverflow = document.body.style.overflow;
    previousHtmlOverflow = document.documentElement.style.overflow;
    previousBodyPaddingRight = document.body.style.paddingRight;
    document.body.style.overflow = 'hidden';
    document.documentElement.style.overflow = 'hidden';
    if (scrollbarGap > 0) {
      document.body.style.paddingRight = `${scrollbarGap}px`;
    }
    const main = document.getElementById('main-content');
    if (main) {
      previousMainOverflow = main.style.overflow;
      main.style.overflow = 'hidden';
    } else {
      previousMainOverflow = null;
    }
  }
  scrollLockCount += 1;
}

function releaseScrollLock() {
  scrollLockCount = Math.max(0, scrollLockCount - 1);
  if (scrollLockCount > 0) return;
  document.body.style.overflow = previousBodyOverflow;
  document.documentElement.style.overflow = previousHtmlOverflow;
  document.body.style.paddingRight = previousBodyPaddingRight;
  const main = document.getElementById('main-content');
  if (main && previousMainOverflow !== null) {
    main.style.overflow = previousMainOverflow;
  }
}

export interface ModalShellProps {
  open: boolean;
  onClose: () => void;
  children: React.ReactNode;
  /** max-w-* Tailwind class for the panel (default max-w-lg). Ignored when bare. */
  maxWidthClassName?: string;
  /** Close when clicking the backdrop (default true) */
  closeOnBackdrop?: boolean;
  /** Close on Escape (default true) */
  closeOnEscape?: boolean;
  /** Extra classes on the surface panel (ignored when bare) */
  panelClassName?: string;
  /**
   * When true, children are the full panel (caller owns surface chrome).
   * ModalShell still portals the overlay and locks page scroll.
   */
  bare?: boolean;
  /** Centered card (default) or bottom sheet on small screens. */
  align?: 'center' | 'bottom-sheet';
  role?: 'dialog' | 'alertdialog';
  /** Stacking class for the overlay (default z-[100]). */
  zClassName?: string;
  overlayClassName?: string;
  'aria-labelledby'?: string;
  'aria-describedby'?: string;
}

/**
 * Full-viewport modal portaled to document.body so overlays are not clipped by
 * OrgLayout's main column (overflow-hidden + relative z-10).
 * Page scroll is locked while open; only the panel content may scroll.
 */
export const ModalShell: React.FC<ModalShellProps> = ({
  open,
  onClose,
  children,
  maxWidthClassName = 'max-w-lg',
  closeOnBackdrop = true,
  closeOnEscape = true,
  panelClassName = '',
  bare = false,
  align = 'center',
  role = 'dialog',
  zClassName = 'z-[100]',
  overlayClassName = '',
  'aria-labelledby': ariaLabelledBy,
  'aria-describedby': ariaDescribedBy,
}) => {
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!open) return;

    previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
    acquireScrollLock();

    const focusFirst = () => {
      const panel = panelRef.current;
      if (!panel) return;
      const focusable = panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR);
      (focusable[0] ?? panel).focus();
    };
    const frame = requestAnimationFrame(focusFirst);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && closeOnEscape) {
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const panel = panelRef.current;
      if (!panel) return;
      const focusable = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
        (el) => !el.hasAttribute('disabled') && el.tabIndex !== -1
      );
      if (!focusable.length) {
        event.preventDefault();
        panel.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    // Block background scroll via wheel/touch outside the panel (iOS / nested overflow).
    const isInsidePanel = (target: EventTarget | null) => {
      const panel = panelRef.current;
      return !!(panel && target instanceof Node && panel.contains(target));
    };

    const handleWheel = (event: WheelEvent) => {
      if (isInsidePanel(event.target)) return;
      event.preventDefault();
    };

    const handleTouchMove = (event: TouchEvent) => {
      if (isInsidePanel(event.target)) return;
      event.preventDefault();
    };

    document.addEventListener('keydown', handleKeyDown);
    document.addEventListener('wheel', handleWheel, { passive: false });
    document.addEventListener('touchmove', handleTouchMove, { passive: false });

    return () => {
      cancelAnimationFrame(frame);
      document.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('wheel', handleWheel);
      document.removeEventListener('touchmove', handleTouchMove);
      releaseScrollLock();
      previouslyFocusedRef.current?.focus?.();
    };
  }, [open, closeOnEscape]);

  if (!open) return null;

  const overlayAlign =
    align === 'bottom-sheet'
      ? 'items-end sm:items-start justify-center sm:pt-[12vh]'
      : 'items-center justify-center p-4';

  const panel = bare ? (
    <div
      ref={panelRef}
      role={role}
      aria-modal="true"
      aria-labelledby={ariaLabelledBy}
      aria-describedby={ariaDescribedBy}
      tabIndex={-1}
      className={`focus:outline-none overscroll-contain ${panelClassName}`}
      onMouseDown={(event) => event.stopPropagation()}
    >
      {children}
    </div>
  ) : (
    <div
      ref={panelRef}
      role={role}
      aria-modal="true"
      aria-labelledby={ariaLabelledBy}
      aria-describedby={ariaDescribedBy}
      tabIndex={-1}
      className={`w-full ${maxWidthClassName} max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto overscroll-contain rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-main)] shadow-2xl p-6 sm:p-8 focus:outline-none ${panelClassName}`}
      onMouseDown={(event) => event.stopPropagation()}
    >
      {children}
    </div>
  );

  const content = (
    <div
      className={`fixed inset-0 ${zClassName} flex overflow-hidden bg-black/60 backdrop-blur-sm ${overlayAlign} ${overlayClassName}`}
      role="presentation"
      onMouseDown={(event) => {
        if (!closeOnBackdrop) return;
        if (event.target === event.currentTarget) onClose();
      }}
    >
      {panel}
    </div>
  );

  if (typeof document === 'undefined') {
    return content;
  }

  return createPortal(content, document.body);
};

export default ModalShell;
