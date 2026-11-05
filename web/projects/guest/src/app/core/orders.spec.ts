import { trackerStage } from './orders';

describe('trackerStage', () => {
  it('maps order states onto Selection, Ordered, Ready, Served and Check Out', () => {
    expect(trackerStage('Cart')).toBe(0);
    expect(trackerStage('Submitted')).toBe(1);
    expect(trackerStage('Accepted')).toBe(1);
    expect(trackerStage('Preparing')).toBe(1);
    expect(trackerStage('Ready')).toBe(2);
    expect(trackerStage('Completed')).toBe(3);
    expect(trackerStage('Closed')).toBe(4);
  });

  it('has no stage for a rejected order', () => {
    expect(trackerStage('Rejected')).toBe(-1);
  });
});
