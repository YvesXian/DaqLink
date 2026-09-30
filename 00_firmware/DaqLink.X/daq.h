/* 
 * File:   daq.h
 * Author: user
 *
 * Created on 2026年9月30日, 下午 2:30
 */

#ifndef DAQ_H
#define	DAQ_H

#include <stdint.h>

#define DAQ_DEFAULT_RATE    100 /* Hz */

#ifdef	__cplusplus
extern "C" {
#endif

    void daq_init(void);
    void daq_start(void);
    void daq_stop(void);
    void daq_set_rate(uint16_t hz);
    uint16_t daq_get_rate(void);
    int daq_is_running(void);
    uint32_t daq_lock(void);
    void daq_unlock(uint32_t saved);

#ifdef	__cplusplus
}
#endif

#endif	/* DAQ_H */

