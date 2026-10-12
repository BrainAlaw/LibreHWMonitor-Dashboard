const MAX_POINTS = 60;

/** Rolling per-key value history used for the traffic chart. */
export class History {
  private readonly series = new Map<string, number[]>();

  push(key: string, value: number): void {
    if (!Number.isFinite(value)) return;
    const values = this.series.get(key) ?? [];
    values.push(value);
    if (values.length > MAX_POINTS) values.splice(0, values.length - MAX_POINTS);
    this.series.set(key, values);
  }

  get(key: string): readonly number[] {
    return this.series.get(key) ?? [];
  }
}
