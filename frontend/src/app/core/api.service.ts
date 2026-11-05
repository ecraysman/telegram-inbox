import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ContactSummaryDto, MarkReadRequest, MessageDto, SendReplyRequest } from './models';

/** REST access to the api (contracts §2). URLs are relative: nginx proxies /api. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  getContacts(): Observable<ContactSummaryDto[]> {
    return this.http.get<ContactSummaryDto[]>(`${this.base}/contacts`);
  }

  getMessages(contactId: string): Observable<MessageDto[]> {
    return this.http.get<MessageDto[]>(`${this.base}/contacts/${encodeURIComponent(contactId)}/messages`);
  }

  markRead(contactId: string, upToMessageId: string): Observable<void> {
    const body: MarkReadRequest = { upToMessageId };
    return this.http.post<void>(`${this.base}/contacts/${encodeURIComponent(contactId)}/read`, body);
  }

  sendReply(contactId: string, text: string): Observable<MessageDto> {
    const body: SendReplyRequest = { text };
    return this.http.post<MessageDto>(`${this.base}/contacts/${encodeURIComponent(contactId)}/messages`, body);
  }

  retry(messageId: string): Observable<MessageDto> {
    return this.http.post<MessageDto>(`${this.base}/messages/${encodeURIComponent(messageId)}/retry`, null);
  }
}
