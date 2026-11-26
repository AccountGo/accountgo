// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/** Shapes and calls for the documents library (/api/documents) and "Download all documents" (/api/documents/exports). */

export type DocumentTargetType = 'bill' | 'invoice' | 'customer' | 'vendor';

export interface DocumentLink {
  targetType: DocumentTargetType;
  targetId: string;
  role: 'source' | 'attachment';
  linkedAt: string;
  locked: boolean;
}

export interface LibraryDocument {
  id: string;
  kind: string;
  status: string;
  title: string;
  fileName: string | null;
  category: string | null;
  documentDate: string | null;
  notes: string | null;
  contentType: string;
  byteCount: number;
  pageCount: number;
  width: number | null;
  height: number | null;
  storedSha256: string;
  uploadedSha256: string;
  storedCopyOptimised: boolean;
  archived: boolean;
  inTrash: boolean;
  filedAt: string;
  createdAt: string;
  links: DocumentLink[];
  canManage: boolean;
  hasLockedLink: boolean;
  hasSourceLink: boolean;
}

export interface DocumentPage {
  items: LibraryDocument[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UploadLimit {
  maxBytes: number;
  maxPdfPages: number;
}

export interface DocumentCapabilities {
  enabled: boolean;
  allowedMimeTypes: string[];
  categories: string[];
  libraryLimit: UploadLimit;
  recordLimit: UploadLimit;
  usedBytes: number;
  storageLimitBytes: number | null;
  canView: boolean;
  canManage: boolean;
}

export interface DocumentExportRun {
  id: string;
  status: 'ready' | 'running' | 'server_finished' | 'failed';
  createdAt: string;
  completedAt: string | null;
  selectedCount: number;
  selectedBytes: number;
  writtenCount: number;
  failureReason: string | null;
  current: boolean;
}

export interface DocumentExportStatus {
  filedCount: number;
  filedBytes: number;
  currentRevision: number;
  latest: DocumentExportRun | null;
}

export const DOCUMENT_CATEGORIES: { value: string; label: string }[] = [
  { value: 'contract', label: 'Contract' },
  { value: 'agreement', label: 'Agreement' },
  { value: 'receipt', label: 'Receipt' },
  { value: 'invoice', label: 'Invoice' },
  { value: 'statement', label: 'Statement' },
  { value: 'tax', label: 'Tax' },
  { value: 'legal', label: 'Legal' },
  { value: 'other', label: 'Other' },
];

export const categoryLabel = (value: string | null | undefined): string =>
  DOCUMENT_CATEGORIES.find((c) => c.value === value)?.label ?? 'Uncategorised';

export const ACCEPTED_FILE_TYPES = 'application/pdf,image/jpeg,image/png';

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1).replace(/\.0$/, '')} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1).replace(/\.0$/, '')} GB`;
}

/** Where a linked record opens in the app. */
export function linkPath(orgSlug: string, link: Pick<DocumentLink, 'targetType' | 'targetId'>): string {
  switch (link.targetType) {
    case 'bill':
      return `/org/${orgSlug}/purchases/${link.targetId}`;
    case 'invoice':
      return `/org/${orgSlug}/sales/${link.targetId}`;
    case 'customer':
      return `/org/${orgSlug}/sales/clients/${link.targetId}`;
    case 'vendor':
      return `/org/${orgSlug}/purchases/vendors/${link.targetId}`;
  }
}

export const linkLabel = (targetType: DocumentTargetType): string =>
  ({ bill: 'Bill', invoice: 'Invoice', customer: 'Client', vendor: 'Vendor' })[targetType];

export interface DocumentProblem {
  code?: string;
  title?: string;
  detail?: string;
  documentId?: string;
  maxBytes?: number;
  maxPdfPages?: number;
  usedBytes?: number;
  limitBytes?: number;
}

/** Plain words for the server's document problem codes. */
export function documentProblemMessage(problem: DocumentProblem | null, fallback: string): string {
  switch (problem?.code) {
    case 'DOCUMENT_TOO_LARGE':
    case 'DOCUMENT_TOO_MANY_PAGES':
      return problem.maxBytes
        ? `Files here can be up to ${formatBytes(problem.maxBytes)} and ${problem.maxPdfPages} pages. Larger files can go in Documents and be linked from there.`
        : 'This file is too large.';
    case 'DOCUMENT_STORAGE_LIMIT':
      return problem.limitBytes != null
        ? `You've used ${formatBytes(problem.usedBytes ?? 0)} of ${formatBytes(problem.limitBytes)} for documents. Permanently delete files you no longer need (files in the trash still count), or upgrade for more space.`
        : 'Your document storage is full.';
    case 'DOCUMENT_RATE_LIMITED':
      return 'Too many uploads in the last hour. Try again later.';
    case 'DOCUMENT_UNSUPPORTED_TYPE':
    case 'DOCUMENT_MEDIA_MISMATCH':
      return 'Only PDF, JPEG and PNG files can be uploaded.';
    case 'DOCUMENT_MALFORMED':
    case 'DOCUMENT_EMPTY':
      return 'This file is empty or damaged.';
    case 'DOCUMENT_DUPLICATE':
      return 'This file is already in Documents.';
    case 'DOCUMENT_LOCKED_BY_POSTED_RECORD':
      return 'Kept as evidence for a posted bill or invoice. You can archive it instead.';
    case 'DOCUMENT_SOURCE_LINK_FIXED':
      return 'This is the scanned source of a bill or invoice. Delete or edit that record to remove it.';
    case 'DOCUMENT_PERMISSION_REQUIRED':
      return "You don't have permission to do that.";
    case 'DOCUMENTS_DISABLED':
      return "Documents isn't available in this workspace yet.";
    case 'DOCUMENT_BUSY':
    case 'DOCUMENT_DECODE_BUSY':
      return 'Busy right now. Try again in a moment.';
    default:
      return problem?.detail || fallback;
  }
}

export async function readProblem(response: Response): Promise<DocumentProblem | null> {
  try {
    return (await response.json()) as DocumentProblem;
  } catch {
    return null;
  }
}
