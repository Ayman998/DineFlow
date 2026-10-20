import { trackerStage } from './orders';

describe('trackerStage', () => {
  it('maps order states onto Selection, Checkout, Preparing and Delivered', () => {
    expect(trackerStage('Cart')).toBe(0);
    expect(trackerStage('Submitted')).toBe(1);
    expect(trackerStage('Accepted')).toBe(2);
    expect(trackerStage('Preparing')).toBe(2);
    expect(trackerStage('Ready')).toBe(2);
    expect(trackerStage('Completed')).toBe(3);
  });

  it('has no stage for a rejected order', () => {
    expect(trackerStage('Rejected')).toBe(-1);
  });
});
