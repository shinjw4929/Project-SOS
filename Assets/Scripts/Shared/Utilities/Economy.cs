// // 생산/건설 요청 처리 시스템
// public bool TryConsumeResources(RefRW<PlayerResources> wallet, ProductionCost cost)
// {
//     // 1. 자원 부족 체크
//     if (wallet.ValueRO.Minerals < cost.Minerals) return false;
//     if (wallet.ValueRO.Gas < cost.Gas) return false;
//
//     // 2. 인구수 부족 체크
//     if (wallet.ValueRO.CurrentPopulation + cost.PopulationCost > wallet.ValueRO.MaxPopulation) return false;
//
//     // 3. 자원 차감 (결제)
//     wallet.ValueRW.Minerals -= cost.Minerals;
//     wallet.ValueRW.Gas -= cost.Gas;
//     wallet.ValueRW.CurrentPopulation += cost.PopulationCost;
//
//     return true;
// }