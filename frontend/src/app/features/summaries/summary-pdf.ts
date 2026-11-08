import { ApiService, saveBlob } from '../../core/api.service';

/** Downloads a summary as a PDF rendered on the server (Arabic and math typeset as on screen). */
export async function downloadSummaryPdf(api: ApiService, summary: { id: string; title: string }): Promise<void> {
  const blob = await api.blob(`/api/summaries/${summary.id}/pdf`);
  const name = summary.title.replace(/[\/:*?"<>|]+/g, ' ').trim().slice(0, 80) || 'summary';
  saveBlob(blob, `${name}.pdf`);
}
