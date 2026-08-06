import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type { CourseCompletionConfirmResult, CourseCompletionPreview, CourseCompletionStatus } from './types';

export function getCourseCompletionStatus(courseId: number, signal?: AbortSignal): Promise<CourseCompletionStatus> {
  return request(`/khoa-hoc/${courseId}/hoan-thanh`, 'GET', undefined, signal);
}

export function previewCourseCompletion(courseId: number, sourceProfileCode: string): Promise<CourseCompletionPreview> {
  return request(`/khoa-hoc/${courseId}/hoan-thanh/preview`, 'POST', { sourceProfileCode });
}

export function confirmCourseCompletion(
  courseId: number,
  previewToken: string,
  completionBusinessDate: string,
  reason: string,
): Promise<CourseCompletionConfirmResult> {
  return request(`/khoa-hoc/${courseId}/hoan-thanh/confirm`, 'POST', {
    previewToken,
    idempotencyKey: crypto.randomUUID(),
    completionBusinessDate,
    reason,
  });
}

async function request<T>(path: string, method: 'GET' | 'POST', body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await apiFetch(`${API_BASE}${path}`, {
    method,
    headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (response.ok) return (await response.json()) as T;
  let message = 'Không thể xử lý chức năng hoàn thành khóa học.';
  try {
    const payload = await response.clone().json() as Record<string, unknown>;
    if (typeof payload.detail === 'string' && payload.detail.length <= 400) message = payload.detail;
  } catch { /* Use bounded generic message. */ }
  throw new Error(`${message} (mã ${response.status}).`);
}
