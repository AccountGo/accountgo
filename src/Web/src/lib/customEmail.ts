export interface ApprovedEmailSender {
  id: string;
  address: string;
  displayName: string;
  domainId: string;
  ready: boolean;
}

export interface EmailCapabilities {
  companyId: number;
  customEmailAvailable: boolean;
  canManageDomains: boolean;
  canManageApprovedSenders: boolean;
  state: string;
  unavailableReason: string | null;
  upgradeAvailable: boolean;
  approvedSenders: ApprovedEmailSender[];
}

export interface CustomEmailIntent {
  senderId: string;
  followUp: boolean;
  attachPdf: boolean;
  additionalRecipients: string[];
  cc: string[];
}
export interface PendingEmailAttempt extends CustomEmailIntent { dispatchId: string }
type PendingStorage = Pick<Storage, 'getItem' | 'setItem'>;

export const pendingEmailKey = (companyId: string, invoiceId: string | number) =>
  `accountgo.email-dispatch.${companyId}.${invoiceId}`;

export function readPendingEmail(storage: PendingStorage, key: string): PendingEmailAttempt | null {
  const raw = storage.getItem(key);
  if (raw === null) return null;
  const parsed = JSON.parse(raw);
  if (!parsed || typeof parsed.dispatchId !== 'string' || typeof parsed.senderId !== 'string'
    || typeof parsed.followUp !== 'boolean' || typeof parsed.attachPdf !== 'boolean'
    || !Array.isArray(parsed.additionalRecipients) || !parsed.additionalRecipients.every((r: unknown) => typeof r === 'string')
    || !Array.isArray(parsed.cc) || !parsed.cc.every((r: unknown) => typeof r === 'string')) {
    throw new Error('The unresolved email attempt needs investigation before another send.');
  }
  return parsed;
}

export function prepareCustomEmailAttempt(storage: PendingStorage, key: string, intent: CustomEmailIntent,
  newId: () => string = () => crypto.randomUUID()): PendingEmailAttempt {
  const prior = readPendingEmail(storage, key);
  if (prior) {
    const { dispatchId: _id, ...original } = prior;
    if (JSON.stringify(original) !== JSON.stringify(intent)) {
      throw new Error('This custom email has an unresolved outcome. Retry with the same sender and recipients before starting another send.');
    }
    return prior;
  }
  const pending = { ...intent, dispatchId: newId() };
  storage.setItem(key, JSON.stringify(pending));
  return pending;
}
